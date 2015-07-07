
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            throw new Exception("Intentional Test Error");
        }
    }
}
