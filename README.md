# Blip
Super-lightweight RPC and pub/sub server using Fleck as a WebSocket transport layer.

If you want to have a JS application RPC on a C# server without WAMP / WCF / JSON-RPC, etc then Blip might just be for you. Blip is super-lightweight and a work in progress. There are many limitations. 

A *JavaScript* client wrapper and *C#* server implementation are provided.

#Client Usage:
```javascript
// Create blip client wrapper.
var blip = Blip("ws://127.0.0.1:9224");

// Subscribe to the Server.Settings topic. Pass in a callback.
blip.sub("Server.Settings", function (settings) { });

// Invoke the "Server.Increment" procedure, and call the callback when complete.
blib.rpc("Server.Increment", [2], function (result) { console.log("2 + 1 = " + result); });

// Invoke the "Server.ErrorWork" procedure with no arguments. Ignore success, and handle failure.
blib.rpc("Server.ErrorWork", [], function(){}, function(err) { console.log("there was an error with this server procedure"); } );
```

#Server Usage:
```c#
using Blip;

namespace Blip.TestServer
{
    class Program
    {
        static BlipWebSocket blip;

        static void Main(string[] args)
        {
            // Create a new Blip websocket server.
            blip = new BlipWebSocket("ws://0.0.0.0:9224");

            // Register a few RPC functions.
            blip.Register("Test.Hello", (Func<String>)Hello);
            blip.Register("Test.Increment", (Func<long, long>)Increment);

            // Register an RPC function on an instance.
            var svc = new Service();
            blip.Register("Test.Service.Names", (Func<List<String>>)svc.ComputeNames);

            // Wait.
            Console.ReadKey();
        }

        static String Hello()
        {
            blip.Publish("Server.Settings", 1, 2, new List<String>() { "Hello", "Subscribers" });
            return "Hello World";
        }

        static long Increment(long a)
        {
            return a + 1;
        }
    }

    public class Service
    {
        public List<String> ComputeNames()
        {
            return new List<string>(){"Bob", "Sally", "Lizzy"};
        }
    }
}

```
