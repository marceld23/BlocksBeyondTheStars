// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// WebGL browser WebSocket bridge for BrowserWebSocketClientTransport.
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

  $BbsWsSendBytesToUnity__deps: ["$BbsWsSendUnityMessage"],
  $BbsWsSendBytesToUnity: function (id, bytes) {
    var binary = "";
    var chunkSize = 0x8000;
    for (var offset = 0; offset < bytes.length; offset += chunkSize) {
      var chunk = bytes.subarray(offset, offset + chunkSize);
      binary += String.fromCharCode.apply(null, chunk);
    }

    BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleMessage", String(id) + "|" + btoa(binary));
  },

  BbsWsConnect__deps: ["$BbsWsSendUnityMessage", "$BbsWsSendBytesToUnity"],
  BbsWsConnect: function (id, urlPtr) {
    var url = UTF8ToString(urlPtr);
    Module.BbsSockets = Module.BbsSockets || {};

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
          BbsWsSendBytesToUnity(id, new Uint8Array(event.data));
          return;
        }

        if (ArrayBuffer.isView(event.data)) {
          BbsWsSendBytesToUnity(id, new Uint8Array(event.data.buffer, event.data.byteOffset, event.data.byteLength));
          return;
        }

        if (typeof event.data === "string") {
          var bytes = new TextEncoder().encode(event.data);
          BbsWsSendBytesToUnity(id, bytes);
          return;
        }

        if (!(event.data instanceof Blob)) {
          BbsWsSendUnityMessage("BbsWebSocketBridge", "HandleError", String(id) + "|unsupported message type");
          return;
        }

        var reader = new FileReader();
        reader.onload = function () {
          BbsWsSendBytesToUnity(id, new Uint8Array(reader.result));
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

  BbsWsSendBase64__deps: ["$BbsWsSendUnityMessage"],
  BbsWsSendBase64: function (id, payloadPtr) {
    var socket = Module.BbsSockets && Module.BbsSockets[id];
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return;
    }

    var payload = UTF8ToString(payloadPtr);
    var binary = atob(payload);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }

    socket.send(bytes);
  },

  BbsWsDisconnect__deps: ["$BbsWsSendUnityMessage"],
  BbsWsDisconnect: function (id) {
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
