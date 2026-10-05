// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The sample filter chips (#2324) never break a word in the middle: every label fits its chip on one line, in each
    /// language's worst case — measured with the game's own font, exactly as the build lays the row out. The rows below
    /// are the kind chips with counts in the languages whose words overflowed the old fixed 112 px chip (German
    /// "Lagerstätten 12", Russian "Месторождения 12", Ukrainian, Spanish, Dutch, Polish, Turkish), plus English.
    /// </summary>
    public sealed class SampleFilterChipsEditModeTests
    {
        private static readonly string[][] Rows =
        {
            new[] { "All 12", "Plants 8", "Animals 5", "Deposits 12" },
            new[] { "Alle 12", "Pflanzen 8", "Tiere 5", "Lagerstätten 12" },
            new[] { "Все 12", "Растения 8", "Животные 5", "Месторождения 12" },
            new[] { "Усі 12", "Рослини 8", "Тварини 5", "Родовища 12" },
            new[] { "Todo 12", "Plantas 8", "Animales 5", "Yacimientos 12" },
            new[] { "Alles 12", "Planten 8", "Dieren 5", "Afzettingen 12" },
            new[] { "Wszystkie 12", "Rośliny 8", "Zwierzęta 5", "Złoża 12" },
            new[] { "Hepsi 12", "Bitkiler 8", "Hayvanlar 5", "Yataklar 12" },
        };

        [Test]
        public void TheInventoryRow_KeepsEveryLabelOnOneLine_AndLeavesRoomForTheEffectButton()
        {
            var parent = new GameObject("row", typeof(RectTransform)).transform;
            try
            {
                foreach (var labels in Rows)
                {
                    var chips = UiKit.AddChipRow(parent, 392f, 168f, 796f, 44f, labels, _ => { }, 8f, 170f, 300f, out float tailX, out float tailW);
                    string row = string.Join(" | ", labels);
                    Assert.That(chips.Length, Is.EqualTo(4), row);
                    Assert.That(tailW, Is.GreaterThanOrEqualTo(170f), row);
                    Assert.That(tailX + tailW, Is.LessThanOrEqualTo(392f + 796f + 0.5f), row);
                    foreach (var chip in chips)
                    {
                        AssertOneLine(chip, row);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        [Test]
        public void TheLabColumn_FitsAllFourChipsIntoItsWidth()
        {
            var parent = new GameObject("column", typeof(RectTransform)).transform;
            try
            {
                foreach (var labels in Rows)
                {
                    var chips = UiKit.AddChipRow(parent, 32f, 120f, 430f, 38f, labels, _ => { }, 6f, 0f, 0f, out float endX, out float tailW);
                    string row = string.Join(" | ", labels);
                    Assert.That(tailW, Is.EqualTo(0f), row);
                    Assert.That(endX - 6f, Is.LessThanOrEqualTo(32f + 430f + 0.5f), row); // the last gap is not part of the row
                    foreach (var chip in chips)
                    {
                        AssertOneLine(chip, row);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        [Test]
        public void MeasuredWidths_GrowWithTheText_AndTheFontShrinksOnlyWhenItMust()
        {
            float all = UiKit.MeasureWidth("Alle 4", 22);
            float deposits = UiKit.MeasureWidth("Lagerstätten 12", 22);
            Assert.That(deposits, Is.GreaterThan(all * 1.8f));
            Assert.That(deposits, Is.GreaterThan(84f), "the word that overflowed the old chip's 84 px text area");

            var go = new GameObject("t", typeof(RectTransform));
            try
            {
                var label = UiKit.AddText(go.transform, 0f, 0f, 200f, 44f, "Lagerstätten 12", 22, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.FitLabelSingleLine(label, 400f, 11, 22);
                Assert.That(label.fontSize, Is.EqualTo(22), "room enough: the full size");
                UiKit.FitLabelSingleLine(label, 84f, 11, 22);
                Assert.That(label.fontSize, Is.LessThan(22).And.GreaterThanOrEqualTo(11));
                Assert.That(UiKit.MeasureWidth(label.text, label.fontSize), Is.LessThanOrEqualTo(84.5f));
                Assert.That(label.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow));
                Assert.That(label.resizeTextForBestFit, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void AssertOneLine(Button chip, string row)
        {
            var label = chip.GetComponentInChildren<Text>();
            var rect = (RectTransform)label.transform;
            Assert.That(label.horizontalOverflow, Is.EqualTo(HorizontalWrapMode.Overflow), row);
            Assert.That(label.resizeTextForBestFit, Is.False, row);
            Assert.That(label.fontSize, Is.GreaterThanOrEqualTo(11), row);
            float measured = UiKit.MeasureWidth(label.text, label.fontSize, label.fontStyle);
            Assert.That(measured, Is.LessThanOrEqualTo(rect.rect.width + 0.5f),
                $"'{label.text}' at {label.fontSize} pt needs {measured:0.#} px in a {rect.rect.width:0.#} px label — {row}");
        }
    }
}
