// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The festival calendar window's door grid (#3316): every door sits where the calendar's layout puts it (row by row,
    /// 6 × 4 for 24), and each looks the way the server's state says — open doors name what they held, waiting doors show
    /// their number, a door whose answer is on the way shows "…", later doors are closed.
    /// </summary>
    public sealed class FestivalCalendarWindowEditModeTests
    {
        private static readonly int[] Layout =
        {
            7, 15, 1, 20, 11, 24, 3, 18, 9, 13, 22, 5, 16, 2, 23, 10, 6, 19, 12, 4, 21, 8, 14, 17,
        };

        private static FestivalCalendarState State()
            => new FestivalCalendarState
            {
                Calendar = "advent",
                Festival = "christmas",
                Active = true,
                Season = 2031,
                Doors = 24,
                Available = 5,
                OpenedMask = (1 << 0) | (1 << 2), // doors 1 and 3
                Contents = new[]
                {
                    new CalendarDoorContent { Door = 1, Items = new[] { "iron_ore", "ice" }, Counts = new[] { 3, 2 } },
                    new CalendarDoorContent { Door = 3, Cosmetic = "skin:christmas" },
                },
            };

        private static CalendarLook Look() => new CalendarLook
        {
            L = key => key,
            ItemName = item => "name:" + item,
            CosmeticName = key => "cosmetic:" + key,
        };

        [Test]
        public void TheDoors_SitInTheLayoutsOrder_SixToARow()
        {
            var parent = new GameObject("grid", typeof(RectTransform)).transform;
            try
            {
                var doors = UiFestivalCalendar.BuildGrid(parent, 40f, 132f, 1200f, 610f, State(), Layout, Look(), _ => { });
                Assert.That(doors.Select(d => d.Door), Is.EqualTo(Layout));

                var first = (RectTransform)doors[0].Button.transform;
                var secondInRow = (RectTransform)doors[1].Button.transform;
                var lastInRow = (RectTransform)doors[5].Button.transform;
                var nextRow = (RectTransform)doors[6].Button.transform;
                Assert.That(secondInRow.anchoredPosition.x, Is.GreaterThan(first.anchoredPosition.x));
                Assert.That(secondInRow.anchoredPosition.y, Is.EqualTo(first.anchoredPosition.y).Within(0.01f));
                Assert.That(lastInRow.anchoredPosition.x + lastInRow.sizeDelta.x, Is.LessThanOrEqualTo(40f + 1200f + 0.5f));
                Assert.That(nextRow.anchoredPosition.x, Is.EqualTo(first.anchoredPosition.x).Within(0.01f), "row two starts under row one");
                Assert.That(nextRow.anchoredPosition.y, Is.LessThan(first.anchoredPosition.y), "row two lies below row one");

                var lastRow = (RectTransform)doors[23].Button.transform;
                Assert.That(-lastRow.anchoredPosition.y + lastRow.sizeDelta.y, Is.LessThanOrEqualTo(132f + 610f + 0.5f), "four rows fit");
                Assert.That(doors.Select(d => d.Button.gameObject.name).First(), Is.EqualTo("Door7"));
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        [Test]
        public void EachDoor_LooksTheWayTheStateSays()
        {
            var parent = new GameObject("grid", typeof(RectTransform)).transform;
            try
            {
                var doors = UiFestivalCalendar.BuildGrid(parent, 0f, 0f, 1200f, 610f, State(), Layout, Look(), _ => { }, pendingDoor: 4)
                    .ToDictionary(d => d.Door);

                Assert.That(doors[1].Status, Is.EqualTo(CalendarDoorStatus.Open));
                Assert.That(doors[1].Summary, Is.EqualTo("name:iron_ore · name:ice"), "an open door names what it held");
                Assert.That(doors[1].Number, Is.Empty);
                Assert.That(doors[3].Status, Is.EqualTo(CalendarDoorStatus.Open));
                Assert.That(doors[3].Summary, Is.EqualTo("cosmetic:skin:christmas"));

                Assert.That(doors[2].Status, Is.EqualTo(CalendarDoorStatus.Available));
                Assert.That(doors[2].Number, Is.EqualTo("2"));
                Assert.That(doors[5].Status, Is.EqualTo(CalendarDoorStatus.Available));
                Assert.That(doors[4].Number, Is.EqualTo("…"), "the door whose answer is on the way");

                Assert.That(doors[6].Status, Is.EqualTo(CalendarDoorStatus.Later));
                Assert.That(doors[24].Status, Is.EqualTo(CalendarDoorStatus.Later));
                Assert.That(doors[24].Number, Is.EqualTo("24"));
                Assert.That(doors.Values.All(d => d.Button != null && d.Button.interactable), Is.True,
                    "every door answers a click — a later one says when it comes");
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        [Test]
        public void AClickedDoor_ReportsItsOwnNumber()
        {
            var parent = new GameObject("grid", typeof(RectTransform)).transform;
            try
            {
                int clicked = 0;
                var doors = UiFestivalCalendar.BuildGrid(parent, 0f, 0f, 1200f, 610f, State(), Layout, Look(), d => clicked = d);
                doors.First(d => d.Door == 5).Button.onClick.Invoke();
                Assert.That(clicked, Is.EqualTo(5));
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        [Test]
        public void TheUnlockLine_SaysWhereTheCosmeticWaits()
        {
            var look = new CalendarLook { L = key => key + " {name}", CosmeticName = key => "N" };
            Assert.That(UiFestivalCalendar.UnlockLine("hudframe:christmas_lights", look), Is.EqualTo("ui.calendar.unlocked_hudframe N"));
            Assert.That(UiFestivalCalendar.UnlockLine("skin:christmas", look), Is.EqualTo("ui.calendar.unlocked_skin N"));
            Assert.That(UiFestivalCalendar.UnlockLine("hat:tall", look), Is.EqualTo("ui.calendar.unlocked N"));
        }

        [Test]
        public void ACosmeticWithoutAName_IsCalledByItsKind()
        {
            Assert.That(CalendarLook.CosmeticNameOf("skin:christmas", null, key => key, _ => false), Is.EqualTo("ui.calendar.cosmetic.skin"));
            Assert.That(CalendarLook.CosmeticNameOf("hudframe:x", null, key => key, _ => false), Is.EqualTo("ui.calendar.cosmetic.hudframe"));
            Assert.That(CalendarLook.CosmeticNameOf("other", null, key => key, _ => true), Is.EqualTo("ui.calendar.cosmetic.other"));
        }
    }
}
