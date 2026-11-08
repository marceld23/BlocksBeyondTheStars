// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Ressaw on the server (Justus' "Alte Städte", #3335/#3337/#3339) — the milky, glowing, corrosive still liquid that floods
/// the buried G.D.S. cities:
/// <list type="bullet">
/// <item><b>Swimming and the oxygen drain</b> need nothing here: <c>ressaw</c> is a still liquid by data
/// (<c>"liquid": true</c>, like oil, #2106), so the client swims in it (<c>WaterProbe</c>) and a head in it drains the
/// tank (<see cref="HeadUnderwater"/>); the fluid pump pulls a cell into the item <c>ressaw</c> (its drop).</item>
/// <item><b>Contact damage</b> (#3335, #3432): feet or head in a Ressaw cell — wading or diving — costs
/// <see cref="RessawDamagePerSecond"/> × the hazard tier × (1 − the worn corrosion resistance) health per second from the
/// first touch (the three-second grace it had is gone: a quick dip went unnoticed), with its own death line, and the
/// natural healing pauses meanwhile. Ressaw is corrosive, not a blow: armour does not help, the
/// suit liners' corrosion resistance (and a toxin ward) does — the best piece counts, capped at 80 %, the same formula as
/// the corrosive air. Off in Creative and at hazard tier Off, like every environmental hazard; aboard, on an EVA and in a
/// vehicle nobody touches it.</item>
/// <item><b>The diving suit</b> (#3337): all four <c>ressawSuit</c> pieces worn (<see cref="SuitEquipment.RessawSuitComplete"/>)
/// keep the Ressaw out completely — no contact damage, and with the head in Ressaw the tank holds (it neither drains nor
/// refills: the suit breathes the Ressaw, not air). Three pieces are not enough. Water still drains the tank as before.</item>
/// <item><b>The lamp's night sight</b> (#3339): while a worn piece with <c>nightSight</c> (the Ressaw lamp) is switched on
/// with the lamp key, the server keeps <see cref="BioEffect.NightSight"/> running at the top level, renewing a short run;
/// switched off or taken off, the lamp's run ends at once. It is one of the three effects a player may run — with three
/// preparations running it waits for a free place — and a stronger or longer night sight the player drank is left alone.</item>
/// </list>
/// Cost per player and tick: two block reads for the Ressaw check (a dictionary lookup per worn piece only while standing
/// in Ressaw), one flag read for the lamp while it is off — no new per-world state, two session fields.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The block key of the liquid.</summary>
    private const string RessawBlockKey = "ressaw";

    /// <summary>Health per second Ressaw takes from the first touch, at the normal hazard tier — slow: a child who keeps
    /// swimming in it has half a minute to climb out.</summary>
    internal const float RessawDamagePerSecond = 3f;

    /// <summary>The level of the lamp's night sight — the top of the curve: "the world around you looks like day".</summary>
    internal const int LampSightLevel = BioRules.MaxLevel;

    /// <summary>How long the lamp's night sight runs when it is started or renewed.</summary>
    internal const float LampSightSeconds = 20f;

    /// <summary>The lamp renews its night sight once less than this is left — a renewal is a state push to the client (the
    /// HUD counts effects down by itself), so it happens about every ten seconds, not every tick.</summary>
    internal const float LampSightRenewBelow = 10f;

    private ushort? _ressawIdCache;

    /// <summary>The numeric id of the Ressaw block, 0 when the content has none.</summary>
    private ushort RessawId => _ressawIdCache ??= _content.GetBlock(RessawBlockKey)?.NumericId.Value ?? 0;

    private bool IsRessawAt(float x, float y, float z)
        => _world.GetBlock(new Vector3i((int)System.Math.Floor(x), (int)System.Math.Floor(y), (int)System.Math.Floor(z))).Value == RessawId;

    /// <summary>The feet stand in a Ressaw cell (wading counts) — the same probe height as the water's.</summary>
    private bool FeetInRessaw(PlayerState p) => IsRessawAt(p.Position.X, p.Position.Y + 0.2f, p.Position.Z);

    /// <summary>The head is in a Ressaw cell — the same probe height as <see cref="HeadUnderwater"/>.</summary>
    private bool HeadInRessaw(PlayerState p) => IsRessawAt(p.Position.X, p.Position.Y + 1.5f, p.Position.Z);

    /// <summary>#3337: the full Ressaw diving suit is worn (all four pieces).</summary>
    private bool RessawSuitWorn(PlayerState p) => SuitEquipment.RessawSuitComplete(_content.Items.Values, key => Wears(p, key));

    /// <summary>#3337: the head is in Ressaw and the full diving suit is worn — the tank holds (the oxygen hook).</summary>
    private bool BreathesInRessaw(PlayerState p) => RessawId != 0 && HeadInRessaw(p) && RessawSuitWorn(p);

    /// <summary>#3335: the player is in Ressaw (its clock runs) — the natural healing pauses (the toxic water's rule).</summary>
    private static bool RessawStinging(PlayerSession session) => session.Hazards.RessawSeconds > 0;

    /// <summary>#3335/#3432: one tick of Ressaw contact — see the class summary. Called from the hazard part of the vitals
    /// loop.</summary>
    private void TickRessaw(PlayerSession session, double dt)
    {
        var p = session.State;
        bool inRessaw = RessawId != 0 && !p.InEva && p.InSpeeder.Length == 0 && !p.AboardShip
            && Rules.TemperatureHazardsEnabledFor(p.ModeOverride) && (FeetInRessaw(p) || HeadInRessaw(p));
        if (!inRessaw || RessawSuitWorn(p))
        {
            session.Hazards.RessawSeconds = 0;
            return;
        }

        session.Hazards.RessawSeconds += dt;
        ShipAiHintOnce(session, "ressaw");
        float loss = (float)(dt * RessawDamagePerSecond * Rules.HazardSeverityFactor) * (1f - CorrosionResistance(p));
        DamagePlayer(p, loss);
        session.Hazards.HazardDeathReason = "@srv.death.ressaw";
    }

    // ---------------- The lamp (#3339) ----------------

    /// <summary>Whether the player wears a lamp the lamp key switches (<see cref="SuitEquipment.IsLamp"/>) — or, with
    /// <paramref name="nightSightOnly"/>, a lamp that gives night sight.</summary>
    private bool WearsLamp(PlayerState p, bool nightSightOnly = false)
    {
        foreach (var stack in p.Equipment.Slots)
        {
            if (stack is { IsEmpty: false } && _content.GetItem(stack.Item) is { } def
                && (nightSightOnly ? def.NightSight : SuitEquipment.IsLamp(def)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#3339: keeps the lamp's night sight running while a night-sight lamp is worn and switched on, and ends it
    /// once the lamp goes off or comes off. Pushes the player state only when the effects changed.</summary>
    private void TickLampSight(PlayerSession session)
    {
        var p = session.State;
        if (!session.LampOn || !WearsLamp(p, nightSightOnly: true))
        {
            if (session.Hazards.LampSight)
            {
                session.Hazards.LampSight = false;
                if (EndLampSight(p))
                {
                    SendPlayerState(session);
                }
            }

            return;
        }

        session.Hazards.LampSight = true;
        if (KeepLampSight(p))
        {
            SendPlayerState(session);
        }
    }

    /// <summary>Starts or renews the lamp's night sight unless a night sight as strong with enough time left already runs
    /// (the lamp's own, or one the player drank). A weaker one gives way to it — the bio lab's rule for a stronger effect of
    /// the same kind. With three other effects running there is no place for it. True when the effects changed.</summary>
    private static bool KeepLampSight(PlayerState p)
    {
        foreach (var e in p.Effects)
        {
            if (e.Effect == BioEffect.NightSight && e.SecondsLeft >= LampSightRenewBelow && e.Level >= LampSightLevel)
            {
                return false;
            }
        }

        if (PlayerEffects.Check(p.Effects, BioEffect.NightSight, LampSightLevel) is not (EffectAddResult.Applied or EffectAddResult.Refreshed))
        {
            return false; // three other effects run — the lamp waits for a free place
        }

        BioLabSystem.StartEffect(p, new ActiveEffect { Effect = BioEffect.NightSight, Level = LampSightLevel, SecondsLeft = LampSightSeconds });
        return true;
    }

    /// <summary>Ends the lamp's own night sight (top level, no catch, at most one run long) — a night sight the player drank
    /// keeps running. True when one ended.</summary>
    private static bool EndLampSight(PlayerState p)
        => p.Effects.RemoveAll(e => e.Effect == BioEffect.NightSight && e.Level == LampSightLevel
            && e.Side == BioSideEffect.None && e.SecondsLeft <= LampSightSeconds) > 0;

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3335): the Ressaw clock of a player.</summary>
    public double RessawSecondsForTest(string playerId) => FindSessionByPlayerId(playerId)?.Hazards.RessawSeconds ?? 0;

    /// <summary>Test seam (#3337): whether the player's tank holds in Ressaw right now (head in it, full suit worn).</summary>
    public bool BreathesInRessawForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && BreathesInRessaw(s.State);
}
