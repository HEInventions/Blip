// Blip-0.0.2.js
// Hardy & Ellis Inventions LTD.
// 04-06-2015
// 
// https://github.com/HEInventions/Blip
// Blip is a super-lightweight RPC and pub/sub server using Fleck as a WebSocket transport layer.
// This file contains a wrapper that exposes nice client-side API functionality.

/**
 * Create a new Blip client that communicates via a websocket. 
 * @param location The address of the server. For instance; ws://127.0.0.1:8080 or a socket itself.
 */
var Blip = function (location) {
    /** Table of RPC call IDs to handler objects . */
    this.__dRPCHandlers = {};

    /** Table of subscription topics to handlers lists. */
    this.__dTopicsToHandlers = {};

    /** A callback ID generator. */
    this.__nextCallbackID = 1;

    // Create from URL or socket.
    this.socket = typeof location == "string" ? new WebSocket(location) : location;

    var self = this;

    /** Helper to dispatch arguments to a JS function. */
    this.__dispatch = function dispatch(handler, args) {
        if (handler === undefined) return;
        try {
            // If not an array, put in an array.
            if (!(typeof args === "object" && args.length !== undefined))
                args = [args];
            // Dispatch.
            handler.apply(null, args);
        }
        catch (err) {
            console.error("Blip: error invoking subscription handler");
            console.error(err);
        }
    };

    /** Helper to clear an RPC call and timeout.*/
    this.__clearRPC = function clearRPC(target) {
        var rpc = self.__dRPCHandlers[target];
        if (rpc !== undefined) {
            clearTimeout(rpc.timer);
            delete self.__dRPCHandlers[target];
        }
    };

    // Handle responses from the server.
    self.socket.onmessage = function (message) {
        
        // Read message as JSON.
        var json = JSON.parse(message.data);
        
        // Handle as a topic.
        if (json.Topic !== undefined) {

            // Get the handlers.
            var handlers = self.__dTopicsToHandlers[json.Topic];
			if (handlers === undefined)
				return;
			
            // For each, apply arguments.
            for (var i = 0; i < handlers.length; ++i)
                self.__dispatch(handlers[i], json.Arguments);
        }

            // Handle as an RPC callback.
        else if (json.Target !== undefined) {

            // Get the handler.
            var rpc = self.__dRPCHandlers[json.Target];
            if (rpc === undefined) {
                console.error("Blip: no handler for '" + json.Target + "'");
                return;
            }

            // Remove from table.
            self.__clearRPC(json.Target);

            // Callback relevant item.
            if (json.Success == true) { self.__dispatch(rpc.success, json.Result); }
            else if (json.Success == false) { self.__dispatch(rpc.failure, json.Result); }
            else { console.error("Blip: malformed server response. Missing success condition"); }
        }

        else {
            console.error("Blip: malformed server packet.");
        }
    };

    /** 
	 * Subscribe to a topic for updates.
	 * @param topic The string name of the topic to listen too.
	 * @param handler The function that is to be called with arguments on update.
	 */
    this.sub = function sub(topic, handler) {

        // Sanitise topic.
        if (topic === null || topic.match(/^ *$/) !== null)
            throw "Blip: cannot subscribe to null or whitespace topic";

        // Push subscription.
        if (self.__dTopicsToHandlers[topic] === undefined)
            self.__dTopicsToHandlers[topic] = [];
        self.__dTopicsToHandlers[topic].push(handler);

        // Chain.
        return self;
    };

    /**
	 * Clear subscriptions all of a particular topic or handler.
	 * @param arg A topic STRING or handler FUNCTION to be removed. If left blank, all are removed.
	 */
    this.clear = function clear(arg) {
        var isTopic = typeof arg === "string";
        var isHandler = typeof arg === "function";

        if (isTopic) delete self.__dTopicsToHandlers[arg];
        else if (isHandler) {
            for (var topic in self.__dTopicsToHandlers) {
                var lst = self.__dTopicsToHandlers[topic];
                var index = lst.indexOf(arg);
                if (index > -1) lst.splice(index, 1);
            }
        }
        else self.__dTopicsToHandlers = {};
        return self;
    };

    /**
	 * Call a remote procedure on a Blip server.
	 * @param target The name of the target procedure / endpoint / function as registered with the server.
	 * @param arguments An array of arguments to be passed to the remote routine.
	 * @param success Optional RPC callback in the case of glorious RPC success.
	 * @param failure Optional RPC callback in the case of dreaded RPC failure.
	 * @param timeout Optional timeout in ms. Default is 60s.
	 */
    this.rpc = function call(target, arguments, success, failure, timeout) {

        // Check args are passed as an array.
        arguments = arguments || [];
        if (!(typeof arguments === "object" && arguments.length !== undefined))
            throw "Arguments need to be encapsulated inside an [] array.";

        // Default failure function.
        failure = failure || function defaultRPCFail(response) { console.error("Blip: Unhandled RPC call '" + target + "' failed: " + response.Message); };

        // Create RPC id.
        var randomID = "rpc_" + self.__nextCallbackID++;

        // Save callback into table.
        self.__dRPCHandlers[randomID] = {
            target: randomID,
            success: success,
            failure: failure,
            timer: setTimeout(function clearRPC() { console.error("Blip: RPC call timed out"); self.__clearRPC(target); }, timeout || 60000)
        };

        // Dispatch RPC to server in requested format.
        self.socket.send(JSON.stringify({ Target: target, Call: randomID, Arguments: arguments }));

        // Chain.
        return self;
    };
}