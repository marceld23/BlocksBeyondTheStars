// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
//
// The browser's local time zone for the WebGL build (#3315): a festival day calendar's doors open at the
// player's own midnight, and .NET's TimeZoneInfo.Local is UTC inside the browser. Date.getTimezoneOffset()
// knows the real zone (daylight saving included). It returns UTC minus local in minutes (-60 in Berlin in
// winter); the C# side (LocalClock) flips the sign and clamps it.
mergeInto(LibraryManager.library, {
  BbsTimezoneOffsetMinutes: function () {
    try {
      return new Date().getTimezoneOffset() | 0;
    } catch (e) {
      return 0; // no clock zone available - the server then counts days in UTC
    }
  }
});
