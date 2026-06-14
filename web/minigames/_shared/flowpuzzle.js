/* Shared rotate-the-tiles flow puzzle used by Circuit Weaver (energy) and Oxygen Loop (life support).
   A guaranteed path is generated from source to receiver, every tile is randomly rotated, and the player
   rotates tiles until flow reaches the receiver. Returns a BBTS.register() definition. */
BBTS.flowPuzzle = function (opts) {
  var N = 1, E = 2, S = 4, Wd = 8;
  var DELTA = { 1: [0, -1], 2: [1, 0], 4: [0, 1], 8: [-1, 0] }, OPP = { 1: 4, 4: 1, 2: 8, 8: 2 };
  function rot(m) { return ((m << 1) | (m >> 3)) & 15; }
  return {
    title: opts.title, desc: opts.desc, difficulty: opts.difficulty || 2,
    hint: { en: "Click a tile to rotate its lines · light up the path to the receiver", de: "Kachel anklicken zum Drehen · Pfad zum Empfänger erleuchten" },
    help: [opts.desc,
           { en: "Click any tile to rotate its lines 90°", de: "Beliebige Kachel anklicken, dreht ihre Leitungen um 90°" },
           { en: "When a connected path glows from source to receiver, it's solved — fewer rotations score higher", de: "Wenn ein verbundener Pfad von der Quelle zum Empfänger leuchtet, ist es gelöst — weniger Drehungen geben mehr Punkte" }],
    create: function (api) {
      var SZ = opts.size || 6, CELL = 64, W = SZ * CELL;
      var ctx = api.canvas(W, W).getContext('2d');
      var mask, src, dst, rots, done;
      function reset() {
        mask = []; for (var i = 0; i < SZ * SZ; i++) mask.push(0);
        rots = 0; done = false; api.hud({ rotations: 0 });
        var y = []; for (var x = 0; x < SZ; x++) y.push(api.rand(SZ));
        var path = [];
        for (var x2 = 0; x2 < SZ; x2++) {
          var er = x2 === 0 ? y[0] : y[x2 - 1];
          var step = er <= y[x2] ? 1 : -1;
          for (var yy = er; ; yy += step) { path.push([x2, yy]); if (yy === y[x2]) break; }
        }
        for (var k = 0; k < path.length - 1; k++) {
          var a = path[k], b = path[k + 1], dx = b[0] - a[0], dy = b[1] - a[1];
          var d = dx === 1 ? E : dx === -1 ? Wd : dy === 1 ? S : N;
          mask[a[1] * SZ + a[0]] |= d; mask[b[1] * SZ + b[0]] |= OPP[d];
        }
        src = path[0]; dst = path[path.length - 1];
        var decoy = [3, 6, 12, 9, 5, 10, 7, 14, 13, 11];
        for (var i2 = 0; i2 < SZ * SZ; i2++) if (mask[i2] === 0) mask[i2] = decoy[api.rand(decoy.length)];
        for (var i3 = 0; i3 < SZ * SZ; i3++) { var rr = api.rand(4); for (var t = 0; t < rr; t++) mask[i3] = rot(mask[i3]); }
        draw();
      }
      function powered() {
        var seen = {}, stack = [src[1] * SZ + src[0]]; seen[stack[0]] = 1;
        while (stack.length) {
          var idx = stack.pop(), cx = idx % SZ, cy = (idx / SZ) | 0, m = mask[idx];
          [N, E, S, Wd].forEach(function (d) {
            if (!(m & d)) return; var nx = cx + DELTA[d][0], ny = cy + DELTA[d][1];
            if (nx < 0 || ny < 0 || nx >= SZ || ny >= SZ) return; var ni = ny * SZ + nx;
            if ((mask[ni] & OPP[d]) && !seen[ni]) { seen[ni] = 1; stack.push(ni); }
          });
        }
        return seen;
      }
      api.pointer(function (p) {
        if (p.type !== 'down' || done) return;
        var cx = (p.x / CELL) | 0, cy = (p.y / CELL) | 0; if (cx < 0 || cy < 0 || cx >= SZ || cy >= SZ) return;
        mask[cy * SZ + cx] = rot(mask[cy * SZ + cx]); rots++; api.hud({ rotations: rots });
        var seen = powered(); draw(seen);
        if (seen[dst[1] * SZ + dst[0]]) { done = true; var rating = rots <= SZ * 1.5 ? 3 : rots <= SZ * 3 ? 2 : 1; api.after(function () { api.complete({ score: Math.max(50, 600 - rots * 15), rating: rating }); }, 250); }
      });
      function draw(seen) {
        seen = seen || powered();
        ctx.clearRect(0, 0, W, W);
        for (var i = 0; i < SZ * SZ; i++) {
          var cx = i % SZ, cy = (i / SZ) | 0, m = mask[i], on = seen[i];
          var px = cx * CELL + CELL / 2, py = cy * CELL + CELL / 2;
          ctx.strokeStyle = 'rgba(70,214,255,0.10)'; ctx.lineWidth = 1; ctx.strokeRect(cx * CELL + 0.5, cy * CELL + 0.5, CELL, CELL);
          ctx.lineWidth = 5; ctx.strokeStyle = on ? opts.glow : 'rgba(70,214,255,0.35)'; ctx.shadowColor = opts.glow; ctx.shadowBlur = on ? 8 : 0;
          [N, E, S, Wd].forEach(function (d) { if (m & d) { ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + DELTA[d][0] * CELL / 2, py + DELTA[d][1] * CELL / 2); ctx.stroke(); } });
          ctx.shadowBlur = 0;
        }
        ctx.fillStyle = opts.glow; ctx.beginPath(); ctx.arc(src[0] * CELL + CELL / 2, src[1] * CELL + CELL / 2, 10, 0, 7); ctx.fill();
        var dp = seen[dst[1] * SZ + dst[0]]; ctx.strokeStyle = dp ? opts.glow : '#7790a0'; ctx.lineWidth = 3;
        ctx.beginPath(); ctx.arc(dst[0] * CELL + CELL / 2, dst[1] * CELL + CELL / 2, 11, 0, 7); ctx.stroke();
      }
      return { start: reset };
    }
  };
};
