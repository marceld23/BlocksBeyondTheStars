// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Guards the SRP Batcher contract for the project's own shaders (#573). The batcher can only keep many
    /// distinct materials in one SetPass call when a shader declares every per-MATERIAL property inside a single
    /// <c>CBUFFER_START(UnityPerMaterial)</c> block, with the same layout in every pass of the SubShader; a
    /// single property left bare silently drops that shader back to one SetPass per material. Because "silently"
    /// is the operative word — nothing at runtime complains — the rule is checked here instead.
    ///
    /// Three angles: the shaders still compile, Unity's own checker reports the URP SubShader as compatible, and
    /// the cbuffer discipline is readable straight from the source. The middle one asks the internal API behind
    /// the Shader Inspector's "SRP Batcher: compatible" line — authoritative, but it can only answer for a
    /// SubShader that has actually been compiled, so in a headless batch run it says "Not initialized" for every
    /// URP SubShader and the test skips itself (see the canary below). The source-level check has no such
    /// dependency and is what keeps CI honest; run this suite from the Editor's Test Runner for the real verdict.
    /// </summary>
    public sealed class ShaderSrpBatcherEditModeTests
    {
        private const string ShaderFolder = "Assets/BlocksBeyondTheStars/Shaders";

        /// <summary>
        /// Shaders that cannot be batcher-compatible, with the reason. Kept explicit so adding one is a
        /// decision someone writes down, not an omission.
        /// </summary>
        private static readonly Dictionary<string, string> ExpectedIncompatible = new Dictionary<string, string>
        {
            // Built-in-RP CG only (no URP SubShader): a single full-screen additive overlay quad drawn once per
            // menu frame, so there is nothing to batch and no reason to port it. See VisorGlass.shader.
            ["VisorGlass"] = "no URP SubShader — Built-in RP CG overlay, one draw",
        };

        private static IEnumerable<Shader> AllProjectShaders()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { ShaderFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null)
                {
                    yield return shader;
                }
            }
        }

        /// <summary>Index of the SubShader tagged for URP, or -1 when the shader has none.</summary>
        private static int UrpSubShaderIndex(Shader shader)
        {
            var tag = new ShaderTagId("RenderPipeline");
            for (int i = 0; i < shader.subshaderCount; i++)
            {
                if (shader.FindSubshaderTagValue(i, tag).name == "UniversalPipeline")
                {
                    return i;
                }
            }

            return -1;
        }

        [Test]
        public void ProjectShaders_CompileWithoutErrors()
        {
            var broken = new List<string>();
            foreach (var shader in AllProjectShaders())
            {
                if (!ShaderUtil.ShaderHasError(shader))
                {
                    continue;
                }

                string messages = string.Join("; ", ShaderUtil.GetShaderMessages(shader)
                    .Where(m => m.severity == ShaderCompilerMessageSeverity.Error)
                    .Select(m => $"{m.file}:{m.line} {m.message}"));
                broken.Add($"{shader.name}: {messages}");
            }

            Assert.That(broken, Is.Empty, "Shaders failed to compile:\n" + string.Join("\n", broken));
        }

        [Test]
        public void UrpSubShaders_AreSrpBatcherCompatible()
        {
            // UnityEditor.ShaderUtil.GetSRPBatcherCompatibilityCode(Shader, int) — internal, and the only way to
            // read what the Shader Inspector shows. 0 = compatible, everything else is a reason code.
            var method = typeof(ShaderUtil).GetMethod(
                "GetSRPBatcherCompatibilityCode",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Shader), typeof(int) },
                modifiers: null);
            if (method == null)
            {
                Assert.Ignore("UnityEditor.ShaderUtil.GetSRPBatcherCompatibilityCode(Shader, int) is gone — this "
                    + "Unity version needs a new probe. The source-level cbuffer test still guards the rule.");
            }

            // Canary: URP's own Lit shader is batcher-compatible by construction. When the probe calls even that
            // one incompatible, it is not reading real data — a URP SubShader that was never compiled in this
            // session answers "Not initialized ()", which is what a batch run (`scripts/run-tests.ps1
            // -Suites UnityEdit`, `-nographics`) gets for every one of them. Failing the build on that would be
            // pure noise, so the run is skipped and the source-level test below — which needs no graphics device
            // — keeps guarding the rule. Run this suite from the Editor's Test Runner window for the real verdict.
            var canary = Shader.Find("Universal Render Pipeline/Lit");
            int canaryIndex = canary == null ? -1 : UrpSubShaderIndex(canary);
            int canaryCode = canaryIndex < 0 ? 0 : (int)method.Invoke(null, new object[] { canary, canaryIndex });
            if (canaryCode != 0)
            {
                Assert.Ignore("SRP Batcher compatibility is unreadable in this session — URP's own Lit shader "
                    + $"reports '{SrpBatcherIssue(canary, canaryIndex, canaryCode)}'. Expected in a batch run; "
                    + "use the Editor's Test Runner window (or the Shader Inspector) for the real verdict.");
            }

            var failures = new List<string>();
            var unexpectedlyFine = new List<string>();
            foreach (var shader in AllProjectShaders())
            {
                string name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(shader));
                bool waived = ExpectedIncompatible.ContainsKey(name);
                int subShader = UrpSubShaderIndex(shader);
                if (subShader < 0)
                {
                    if (!waived)
                    {
                        failures.Add($"{name}: no URP SubShader (add one, or waive it in ExpectedIncompatible)");
                    }

                    continue;
                }

                int code = (int)method.Invoke(null, new object[] { shader, subShader });
                if (code != 0 && !waived)
                {
                    failures.Add($"{name}: SRP Batcher incompatible ({SrpBatcherIssue(shader, subShader, code)}) — "
                        + "declare every property from the Properties block inside CBUFFER_START(UnityPerMaterial), "
                        + "identically in every pass");
                }
                else if (code == 0 && waived)
                {
                    unexpectedlyFine.Add(name);
                }
            }

            Assert.That(failures, Is.Empty, "SRP Batcher regressions:\n" + string.Join("\n", failures));
            Assert.That(unexpectedlyFine, Is.Empty,
                "These shaders are batcher-compatible now — drop them from ExpectedIncompatible: "
                + string.Join(", ", unexpectedlyFine));
        }

        /// <summary>
        /// The human-readable reason behind a non-zero compatibility code — the same sentence the Material
        /// Inspector prints, e.g. "Material property is found in another cbuffer than UnityPerMaterial (_Color)".
        /// Falls back to the bare code when the (internal, signature-unstable) accessor isn't there.
        /// </summary>
        private static string SrpBatcherIssue(Shader shader, int subShaderIndex, int code)
        {
            foreach (var candidate in typeof(ShaderUtil).GetMethods(BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (!candidate.Name.Contains("SRPBatcher") || candidate.ReturnType != typeof(string))
                {
                    continue;
                }

                var parameters = candidate.GetParameters();
                object[] args = parameters.Length == 2 ? new object[] { shader, subShaderIndex }
                    : parameters.Length == 3 ? new object[] { shader, subShaderIndex, code }
                    : null;
                if (args == null)
                {
                    continue;
                }

                try
                {
                    string reason = (string)candidate.Invoke(null, args);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        return reason;
                    }
                }
                catch (TargetInvocationException)
                {
                    // Wrong overload for this Unity version — fall through to the next candidate.
                }
            }

            return "code " + code;
        }

        [Test]
        public void UrpPasses_DeclareMaterialPropertiesInsideUnityPerMaterial()
        {
            var failures = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { ShaderFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                string source = File.ReadAllText(path); // asset paths are relative to the project folder = CWD

                string urp = UrpSubShaderSource(source);
                if (urp == null)
                {
                    continue; // Built-in-RP-only shader (waived above) — the batcher never sees it.
                }

                var cbufferTextRanges = CbufferTextRanges(urp);
                foreach (string property in MaterialPropertyNames(source))
                {
                    // A texture property contributes _Name_ST / _Name_TexelSize / _Name_HDR constants; the
                    // TEXTURE2D/SAMPLER handles themselves must stay OUT of the cbuffer, so they aren't matched.
                    var declaration = new Regex(
                        @"^[ \t]*(?:float|half|fixed|int|uint|bool)[1-4]?(?:x[1-4])?[ \t]+"
                        + Regex.Escape(property) + @"(?:_ST|_TexelSize|_HDR)?[ \t]*;",
                        RegexOptions.Multiline);
                    foreach (Match match in declaration.Matches(urp))
                    {
                        if (!cbufferTextRanges.Any(r => match.Index >= r.Start && match.Index < r.End))
                        {
                            failures.Add($"{name}: '{match.Value.Trim()}' is a material property declared outside "
                                + "CBUFFER_START(UnityPerMaterial) — the SRP Batcher skips the whole shader for it");
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty, "Material properties outside UnityPerMaterial:\n"
                + string.Join("\n", failures));
        }

        /// <summary>Property names from the shader's top-level <c>Properties { ... }</c> block.</summary>
        private static IEnumerable<string> MaterialPropertyNames(string source)
        {
            var block = Regex.Match(source, @"^\s*Properties\s*\{", RegexOptions.Multiline);
            if (!block.Success)
            {
                yield break; // e.g. VertexColorOpaque: vertex colours only, no material properties at all
            }

            string body = BracedBody(source, block.Index + block.Length - 1);
            foreach (Match property in Regex.Matches(body, @"^[ \t]*(_\w+)\s*\(", RegexOptions.Multiline))
            {
                yield return property.Groups[1].Value;
            }
        }

        /// <summary>The source of the SubShader tagged "UniversalPipeline", or null if there is none.</summary>
        private static string UrpSubShaderSource(string source)
        {
            foreach (Match subShader in Regex.Matches(source, @"^\s*SubShader\s*\{", RegexOptions.Multiline))
            {
                string body = BracedBody(source, subShader.Index + subShader.Length - 1);
                if (Regex.IsMatch(body, "\"RenderPipeline\"\\s*=\\s*\"UniversalPipeline\""))
                {
                    return body;
                }
            }

            return null;
        }

        /// <summary>Text between the brace at <paramref name="openBrace"/> and its match.</summary>
        private static string BracedBody(string source, int openBrace)
        {
            int depth = 0;
            for (int i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}' && --depth == 0)
                {
                    return source.Substring(openBrace + 1, i - openBrace - 1);
                }
            }

            throw new InvalidOperationException("Unbalanced braces in shader source at index " + openBrace);
        }

        private readonly struct TextRange
        {
            public TextRange(int start, int end)
            {
                Start = start;
                End = end;
            }

            public int Start { get; }

            public int End { get; }
        }

        private static List<TextRange> CbufferTextRanges(string source)
        {
            var ranges = new List<TextRange>();
            foreach (Match start in Regex.Matches(source, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)"))
            {
                int end = source.IndexOf("CBUFFER_END", start.Index, StringComparison.Ordinal);
                ranges.Add(new TextRange(start.Index, end < 0 ? source.Length : end));
            }

            return ranges;
        }
    }
}
