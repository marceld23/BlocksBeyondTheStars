// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// WebGL browser WebSocket bridge for BrowserWebSocketClientTransport.
//
// #1531: bytes cross the managed/JS boundary through the WASM heap, not through base64 strings. Outbound:
// C# hands a pointer + length and the socket copies straight out of HEAPU8. Inbound: frames are parked per
// socket as Uint8Array views; C# polls the pending length, allocates the exact array and BbsWsReadPending
// copies the frame into it (one copy, the one the managed array needs anyway). Only open/close/error still
// go through SendMessage strings.
mergeInto(LibraryManager.library, {
  $BbsWsSendUnityMessage: function (objectName, methodName, payload) {
    if (typeof SendMessage === "function") {
      SendMessage(objectName, methodName, payload);
      return;
    }

    if (Module && typeof Module.SendMessage === "function") {
      Module.SendMessage(objectName, methodName, payload);
      return;
    }

    console.error("Unity SendMessage bridge is not available for " + objectName + "." + methodName);
  },

  $BbsWsPark: function (id, bytes) {
    var queues = Module.BbsInbound = Module.BbsInbound || {};
    var q = queues[id] = queues[id] || [];
    q.push(bytes);
  },

  BbsWsConnect__deps: ["$BbsWsSendUnityMessage", "$BbsWsPark"],
  BbsWsConnect: function (id, urlPtr) {
    var url = UTF8ToString(urlPtr);
    Module.BbsSockets = Module.BbsSockets || {};
    Module.BbsInbound = Module.BbsInbound || {};
    Module.BbsInbound[id] = [];

    try {
      var socket = new WebSocket(url);
      socket.binaryType = "arraybuffer";
      Module.BbsSockets[id] = socket;

      socket.onopen = function () {
        BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleOpen", String(id));
      };

      socket.onclose = function () {
        BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleClose", String(id));
        delete Module.BbsSockets[id];
      };

      socket.onerror = function () {
        BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleError", String(id) + "|socket error");
      };

      socket.onmessage = function (event) {
        if (event.data instanceof ArrayBuffer) {
          BbsWsPark(id, new Uint8Array(event.data)); // a view over the frame's own buffer — no copy
          return;
        }

        if (ArrayBuffer.isView(event.data)) {
          BbsWsPark(id, new Uint8Array(event.data.buffer, event.data.byteOffset, event.data.byteLength));
          return;
        }

        if (typeof event.data === "string") {
          BbsWsPark(id, new TextEncoder().encode(event.data));
          return;
        }

        if (!(event.data instanceof Blob)) {
          BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleError", String(id) + "|unsupported message type");
          return;
        }

        var reader = new FileReader();
        reader.onload = function () {
          BbsWsPark(id, new Uint8Array(reader.result));
        };
        reader.onerror = function () {
          BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleError", String(id) + "|message decode failed");
        };
        reader.readAsArrayBuffer(event.data);
      };
    } catch (e) {
      BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleError", String(id) + "|" + String(e));
      BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleClose", String(id));
    }
  },

  // Sends `length` bytes starting at `ptr` in the WASM heap. WebSocket.send copies the view's bytes
  // synchronously, so the managed array may be reused the moment this returns.
  BbsWsSendBytes: function (id, ptr, length) {
    var socket = Module.BbsSockets && Module.BbsSockets[id];
    if (!socket || socket.readyState !== WebSocket.OPEN || length <= 0) {
      return;
    }

    socket.send(HEAPU8.subarray(ptr, ptr + length));
  },

  // Length of the oldest parked inbound frame, 0 when none.
  BbsWsPendingLength: function (id) {
    var q = Module.BbsInbound && Module.BbsInbound[id];
    return q && q.length > 0 ? q[0].length : 0;
  },

  // Copies the oldest parked frame into the managed array at `ptr` (capacity `capacity` bytes) and drops it.
  // Returns the frame length, or -1 when the array is too small (the frame stays parked).
  BbsWsReadPending: function (id, ptr, capacity) {
    var q = Module.BbsInbound && Module.BbsInbound[id];
    if (!q || q.length === 0) {
      return 0;
    }

    var frame = q[0];
    if (frame.length > capacity) {
      return -1;
    }

    HEAPU8.set(frame, ptr);
    q.shift();
    return frame.length;
  },

  BbsWsDisconnect__deps: ["$BbsWsSendUnityMessage"],
  BbsWsDisconnect: function (id) {
    if (Module.BbsInbound) {
      delete Module.BbsInbound[id];
    }

    var socket = Module.BbsSockets && Module.BbsSockets[id];
    if (!socket) {
      return;
    }

    try {
      socket.close();
    } catch (e) {
    }

    delete Module.BbsSockets[id];
  }
});
