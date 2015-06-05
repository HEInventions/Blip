# Blip
Super-lightweight RPC and pub/sub server using Fleck as a WebSocket transport layer.

If you want to have a JS application RPC on a C# server without WAMP / WCF / JSON-RPC, etc then Blip might just be for you. Blip is super-lightweight and a work in progress. There are many limitations. 

A *JavaScript* client wrapper and *C#* server implementation are provided.

![Blip](https://img.shields.io/badge/nuget-v0.0.2-blue.svg)

#Client Usage:
```javascript
// Create Blip client wrapper around a websocket.
var ws = new WebSocket("ws://127.0.0.1:9224");
ws.onopen = function () {
	
	// Now we have an active socket, start blip.
	var blip = new Blip(ws);
	
	// Subscribe to the Server.Settings topic. Pass in a callback.
	blip.sub("Test.Settings", function (settings) { console.log(settings); });
	
	// Say hello.
	blip.rpc("Test.Hello");
	
	// Invoke the "Server.Increment" procedure, and call the callback when complete.
	var arg = 26;
	blip.rpc("Test.Increment", [arg], function (result) { console.log(arg + " + 1 = " + result); });
	
	// Invoke the "Server.ErrorWork" procedure with no arguments. Ignore success, and handle failure.
	blip.rpc("Test.ThrowError", [], function (result) { },
	function (err) { console.log(err.Message + "\n" + err.Stacktrace); });
}
```

#Server Usage:
```c#
using Blip;

namespace Blip.TestServer
{
    /// <summary>
    /// Little test for the Blip websocket server.
    /// </summary>
    class Program
    {
        /// <summary>Server reference.</summary>
        public static BlipWebSocket blip;

        /// <summary>Application entrypoint</summary>
        public static void Main(string[] args)
        {
            // Create a new Blip websocket server.
            blip = new BlipWebSocket("ws://0.0.0.0:9224");

            // Register a few RPC functions.
            blip.Register("Test.Hello", (Func<String>)Hello);
            blip.Register("Test.Increment", (Func<long, long>)Increment);

            // Register an RPC function on an instance.
            var svc = new Service();
            blip.Register("Test.Service.Names", (Func<List<String>>)svc.ComputeNames);
            blip.Register("Test.ThrowError", (Action)svc.Error);

            // Wait.
            Console.ReadKey();
        }

        /// <summary>Publish server settings and return some arbitary text.</summary>
        public static String Hello()
        {
            blip.Publish("Test.Settings", 1, 2, new List<String>() { "Hello", "Subscribers", "Here", "Are", "Settings" });
            return "Hello World";
        }

        /// <summary>Add one number to another and return the result.</summary>
        public static long Increment(long a)
        {
            return a + 1;
        }
    }

    /// <summary>
    /// Demonstrates Blip running on an instance.
    /// </summary>
    public class Service
    {
        /// <summary>
        /// Get some names.
        /// </summary>
        /// <returns></returns>
        public List<String> ComputeNames()
        {
            return new List<string>(){"Bob", "Sally", "Lizzy"};
        }

        /// <summary>
        /// Throw an error.
        /// </summary>
        public void Error()
        {
            throw new Exception("Some error");
        }
    }
```
