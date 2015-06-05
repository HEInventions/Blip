using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using Fleck;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Blip
{
    /// <summary>
    /// Super lightweight RPC / PUBSUB server.
    /// </summary>
    public abstract class Blip
    {
        /// <summary>
        /// Table of handlers to RPC registered delegates.
        /// </summary>
        protected Dictionary<String, Delegate> RegisteredServices;

        /// <summary>
        /// Location of this Blip service as a string. For example, a websocket woud be: ws://0.0.0.0:1234/hello
        /// </summary>
        public virtual String Location { get { throw new NotImplementedException(); } }

        /// <summary>
        /// Raised when Blip logs a warning message.
        /// </summary>
        public Action<Blip, String> LogWarning;

        /// <summary>
        /// Create a new blip service.
        /// </summary>
        public Blip()
        {
            RegisteredServices = new Dictionary<String, Delegate>();
        }

        /// <summary>
        /// Register a delegate as an RPC delegate with this server.
        /// </summary>
        /// <remarks>This will overwrite existing procedures with the same name without warning.</remarks>
        /// <param name="target">The name to register this service with.</param>
        /// <param name="function">The function, method, or lambda.</param>
        public virtual void Register(String target, Delegate function)
        {
            // Sanity check.
            if (String.IsNullOrWhiteSpace(target)) throw new ArgumentNullException("RPC target cannot be null");
            if (function == null) throw new ArgumentNullException("RPC delegate cannot be null");

            // Place in the registered table.
            RegisteredServices[target] = function;
        }

        /// <summary>
        /// Unregister a delegate intended to handle a particular operation.
        /// </summary>
        /// <param name="target">The RPC target of the procedure to remove.</param>
        /// <returns>True if removed successfully, false if not.</returns>
        public bool Unregister(String target)
        {
            // Remove it from the RPC table if it exists.
            return RegisteredServices.Remove(target);
        }

        /// <summary>
        /// Publish data to a topic. This will be sent to all connected subscribers.
        /// </summary>
        /// <param name="topic">The name of the topic to publish too.</param>
        /// <param name="data">The arguments to push to the topic.</param>
        public abstract void Publish(String topic, params object[] arguments);
    }

    /// <summary>
    /// Super lightweight RPC / PUBSUB server using Fleck as a WebSocket transport layer.
    /// </summary>
    /// <remarks>
    /// The service parameter types float and Int32 are not supprted by the JSON conversion.
    /// This is because there is not promise that these types can be converted from dynamic JSON.
    /// 
    /// 
    /// RPC request messages are as follows:
    ///     {
    ///         Target    : "procedurename",    // Procedure target.
    ///         Arguments : [arg1, arg2],       // Arguments as a list of primitive JSON types.
    ///         Call      : "responseID",       // Response ID for callbacks - always called with success / failure.
    ///         TODO: //async   : true,               // Should this method be run in a separate thread? 
    ///     }
    ///    
    /// RPC response messages are as follows:
    ///     {
    ///         Target   : "responseID",        // Passed in with the request.
    ///         Success  : true,                // True or False based on sucess or execption.
    ///         Result   : data,                // Result of RPC procedure converted to JSON. 
    ///                                         // If success == false, this contains the error message.
    ///     }
    /// 
    /// RPC publish message are as follows:
    ///     {
    ///         Topic     : name,               // The name of the topic to publish too.
    ///         Arguments : [data],             // List of data items to be published.
    ///     }
    /// </remarks>
    public class BlipWebSocket : Blip, IDisposable
    {
        #region Message Packets
        /// <summary>Used to parse incoming message requests.</summary>
        private class BlipRequest
        {
            public String Target;
            public String Call;
            public object[] Arguments;

            public void Validate()
            {
                if (String.IsNullOrWhiteSpace(Target))
                    throw new Exception("Missing or malformed RPC procedure target argument");
                if (String.IsNullOrWhiteSpace(Call))
                    throw new Exception("Missing or malformed RPC response handler id");
            }
        }

        /// <summary>Returned to callers after an RPC call.</summary>
        private class BlipResponse
        {
            public String Target;       // The callback target on the client.
            public bool Success;        // Did the server request complete sucessfully.
            public object Result;       // The result of the server request.
        }

        /// <summary>Sent to all clients as subscribers</summary>
        private class BlipPublish
        {
            public String Topic;
            public object[] Arguments;
        }
        #endregion

        /// <summary>
        /// Location of this Blip service as a string. For example, a websocket woud be: ws://0.0.0.0:1234/hello
        /// </summary>
        public override String Location { get { return Server.Location; } }

        /// <summary>
        /// Reference to the websocket server.
        /// </summary>
        private WebSocketServer Server;

        /// <summary>
        /// Table of currently connected clients.
        /// </summary>
        private List<IWebSocketConnection> Clients;

        /// <summary>
        /// Types that are not permissable as dynamic type conversions from JSON.
        /// These cannot be parameters in registered services.
        /// </summary>
        private String[] DisallowedTypes = new String[] { typeof(Int32).FullName, typeof(float).FullName, typeof(Single).FullName, typeof(byte).FullName, typeof(short).FullName };

        /// <summary>
        /// Create a new BlipWebSocket.
        /// </summary>
        /// <param name="location">The location to create the service at. For example, a websocket woud be: ws://0.0.0.0:1234/hello</param>
        public BlipWebSocket(String location) : base()
        {
            Clients = new List<IWebSocketConnection>();
            Server = new WebSocketServer(location);
            Server.Start(socket =>
                {
                    socket.OnOpen    = ()            => { lock (Clients) Clients.Add(socket); };
                    socket.OnClose   = ()            => { lock (Clients) Clients.Remove(socket); };
                    socket.OnError   = (Exception e) => { lock (Clients) Clients.Remove(socket); };
                    socket.OnMessage = (String data) => { HandleMessage(socket, data);  };
                });
        }

        /// <summary>
        /// Register a delegate as an RPC delegate with this server.
        /// </summary>
        /// <remarks>This will overwrite existing procedures with the same name without warning.</remarks>
        /// <param name="target">The name to register this service with.</param>
        /// <param name="function">The function, method, or lambda.</param>
        public override void Register(String target, Delegate function)
        {
            // Sanity.
            if (function == null) throw new ArgumentNullException("RPC delegate cannot be null");

            // Check method does not contain Int32 or float parameters.
            var args = function.GetMethodInfo().GetParameters();
            var errors = args.Count(p => DisallowedTypes.Contains(p.ParameterType.FullName));
            if (errors > 0)
                throw new Exception("BlipWebSocket cannot support parameters with type: " + String.Join(", ", DisallowedTypes.Select(t=>t.ToString())));

            // Base.
            base.Register(target, function);
        }

        /// <summary>
        /// Handle incoming data from the websocket.  
        /// </summary>
        /// <remarks>Check it is valid, handle if RPC.</remarks>
        /// <param name="client">The client connection.</param>
        /// <param name="jsonMessage">The message payload as JSON.</param>
        private void HandleMessage(IWebSocketConnection client, String jsonMessage)
        {
            // Convert to BlipRequest.
            BlipRequest request = null;
            try
            {
                request = JsonConvert.DeserializeObject<BlipRequest>(jsonMessage);
                request.Validate();
            }
            catch (Exception e)
            {
                if (LogWarning != null)
                    LogWarning(this, "Dropped bad Blip request from " + client.ConnectionInfo.ClientIpAddress);
                return;
            }

            // Locate target delegate.
            Delegate target;
            if (!RegisteredServices.TryGetValue(request.Target, out target))
            {
                if (LogWarning!=null)
                    LogWarning(this, "Missing RPC registered handler for target from " + client.ConnectionInfo.ClientIpAddress);
                return;
            }

            // Dynamic invoke.
            String responseJson = null;
            try
            {
                var result = target.DynamicInvoke(request.Arguments);
                responseJson = JsonConvert.SerializeObject(new BlipResponse() { Target = request.Call,Success = true, Result = result });
            }
            catch (Exception e)
            {
                var err = e.InnerException;
                if (err != null) e = err;
                responseJson = JsonConvert.SerializeObject(new BlipResponse() { Target = request.Call, Success = false, Result = new { Message = e.Message, Stacktrace = e.StackTrace} });
            }

            // Pass it back.
            DispatchData(client, responseJson);
        }

        /// <summary>
        /// Publish data to a topic. This will be sent to all connected subscribers.
        /// </summary>
        /// <param name="topic">The name of the topic to publish too.</param>
        /// <param name="data">The arguments to push to the topic.</param>
        public override void Publish(String topic, params object[] arguments)
        {
            // Prepare response.
            var topicJson = JsonConvert.SerializeObject(new BlipPublish() { Topic = topic, Arguments = arguments });

            // For each client.
            lock (Clients)
            {
                foreach (var client in Clients)
                    DispatchData(client, topicJson);
            }
        }

        /// <summary>
        /// Attempt to send data to a client.
        /// </summary>
        /// <param name="client">The client to send data too.</param>
        /// <param name="data">The data to send.</param>
        private void DispatchData(IWebSocketConnection client, String data)
        {
            try
            {
                client.Send(data);
            }
            catch (Exception e)
            {
                if (LogWarning!=null)
                    LogWarning(this, "Error sending data to " + client.ConnectionInfo.ClientIpAddress);
                return;
            }
        }

        /// <summary>
        /// Tear down this server and free memory.
        /// </summary>
        public void Dispose()
        {
            // Tear down server.
            if (Server != null)
                Server.Dispose();
            Server = null;

            // Clear clients.
            lock (Clients) Clients.Clear();
        }
    }
}
