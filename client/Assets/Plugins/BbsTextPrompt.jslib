// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Browser text-entry fallback for the WebGL build on TOUCH devices: Unity's uGUI InputField cannot
// summon a soft keyboard in the browser (TouchScreenKeyboard is unsupported on WebGL), so a tapped
// field would be dead on a tablet. window.prompt() opens the OS keyboard on every mobile browser and
// blocks until the player confirms — crude but universally supported. Desktop browsers never hit this
// path (the bridge only attaches on touch devices, where a hardware keyboard is absent).
mergeInto(LibraryManager.library, {
  // Shows a modal browser prompt. Returns the entered text, or the previous value when cancelled.
  BbsPromptText: function (labelPtr, currentPtr) {
    var label = UTF8ToString(labelPtr);
    var current = UTF8ToString(currentPtr);
    var result = null;
    try {
      result = window.prompt(label, current);
    } catch (e) {
      result = null; // some embedded webviews forbid prompt() — treat as cancel
    }
    if (result === null) {
      result = current; // cancel keeps the old value
    }
    var size = lengthBytesUTF8(result) + 1;
    var buffer = _malloc(size);
    stringToUTF8(result, buffer, size);
    return buffer;
  }
});
