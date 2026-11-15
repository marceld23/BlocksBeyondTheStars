// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3689: the editors' automatic drafts (<see cref="EditorDraftStore"/>) — one per editor, written atomically after the
/// work rested, read back exactly, removed on save, and never in the way: a missing, broken, foreign or oversized draft
/// is simply not offered.
/// </summary>
public sealed class EditorDraftStoreTests : IDisposable
{
    private static readonly DateTime When = new DateTime(2026, 11, 15, 16, 30, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbs-drafts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private EditorDraftStore Store(string key = "ship", long maxBytes = EditorDraftStore.DefaultMaxBytes)
        => new EditorDraftStore(_root, key, maxBytes);

    [Fact]
    public void Write_ThenTryLoad_GivesTheWorkBackExactly()
    {
        var store = Store();
        string work = "{\"cells\":[1,2,3]}\nsecond line\twith a tab · ümlaut · 漢字";
        Assert.Equal(DraftWriteResult.Written, store.Write(work, "My cruiser", When));
        Assert.True(store.HasDraft);

        Assert.True(Store().TryLoad(out var draft)); // a fresh store (the next opening) reads it
        Assert.NotNull(draft);
        Assert.Equal(work, draft!.Content);
        Assert.Equal("My cruiser", draft.Title);
        Assert.Equal(When, draft.SavedUtc);
        Assert.False(File.Exists(store.FilePath + ".tmp")); // the temp file was swapped in
    }

    [Fact]
    public void Write_ReplacesTheOldDraft_AndSkipsAnUnchangedOne()
    {
        var store = Store();
        store.Write("one", "A", When);
        Assert.Equal(DraftWriteResult.Written, store.Write("two", "B", When));
        Assert.Equal(DraftWriteResult.Unchanged, store.Write("two", "B", When));
        Assert.True(store.TryLoad(out var draft));
        Assert.Equal("two", draft!.Content);
    }

    [Fact]
    public void ATitleWithLineBreaksOrTabs_StaysOnTheHeaderLine()
    {
        var store = Store();
        store.Write("work", "My\ncruiser\twith tab", When);
        Assert.True(store.TryLoad(out var draft));
        Assert.Equal("My cruiser with tab", draft!.Title);
        Assert.Equal("work", draft.Content);
    }

    [Fact]
    public void Clear_RemovesTheDraft()
    {
        var store = Store();
        store.Write("work", null, When);
        store.Clear();
        Assert.False(store.HasDraft);
        Assert.False(store.TryLoad(out _));
        Assert.Equal(DraftWriteResult.Written, store.Write("work", null, When)); // "unchanged" is forgotten with the file
    }

    [Fact]
    public void EachEditor_HasItsOwnDraft()
    {
        Store("ship").Write("ship work", null, When);
        Store("texture").Write("texture work", null, When);
        Assert.True(Store("ship").TryLoad(out var ship));
        Assert.True(Store("texture").TryLoad(out var texture));
        Assert.Equal("ship work", ship!.Content);
        Assert.Equal("texture work", texture!.Content);
    }

    [Theory]
    [InlineData("../../evil", "______evil")]
    [InlineData("tool_looks", "tool_looks")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("", "editor")]
    public void TheEditorKey_NeverLeavesTheDraftsFolder(string key, string expected)
    {
        Assert.Equal(expected, EditorDraftStore.SafeKey(key));
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(Path.GetFullPath(Store(key).FilePath)));
    }

    [Fact]
    public void NoDraft_IsNoDraft()
    {
        Assert.False(Store().HasDraft);
        Assert.False(Store().TryLoad(out var draft));
        Assert.Null(draft);
    }

    [Theory]
    [InlineData("just some text")]
    [InlineData("")]
    [InlineData("bbs-draft/1\tnot-a-number\t4\ttitle\nwork")]
    [InlineData("bbs-draft/2\t638668584000000000\t4\ttitle\nwork")]
    [InlineData("bbs-draft/1\t638668584000000000\t99\ttitle\nwork")] // cut short
    [InlineData("bbs-draft/1\t638668584000000000\t4\ttitle\nwork and more")] // edited
    [InlineData("bbs-draft/1\t638668584000000000\t4\nwork")] // header too short
    public void ABrokenOrForeignDraft_IsNotOffered(string text)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Store().FilePath, text, new UTF8Encoding(false));
        Assert.True(Store().HasDraft);
        Assert.False(Store().TryLoad(out var draft));
        Assert.Null(draft);
    }

    [Fact]
    public void SetAside_MovesABrokenDraftOutOfTheWay_WithoutDestroyingIt()
    {
        var store = Store();
        Directory.CreateDirectory(_root);
        File.WriteAllText(store.FilePath, "garbage");
        Assert.True(store.SetAside());
        Assert.False(store.HasDraft);
        Assert.Equal("garbage", File.ReadAllText(store.FilePath + ".broken"));

        File.WriteAllText(store.FilePath, "garbage 2");
        Assert.True(store.SetAside()); // an older set-aside draft is replaced
        Assert.Equal("garbage 2", File.ReadAllText(store.FilePath + ".broken"));
        Assert.False(store.SetAside()); // nothing left to move
    }

    [Fact]
    public void TooLarge_IsNotWritten_AndTheOldDraftStays()
    {
        var store = Store(maxBytes: 16);
        Assert.Equal(DraftWriteResult.Written, store.Write("small", null, When));
        store.MarkDirty(0);
        Assert.Equal(DraftWriteResult.TooLarge, store.Write(new string('x', 17), null, When));
        Assert.False(store.Pending); // no retry every frame
        Assert.True(store.TryLoad(out var draft));
        Assert.Equal("small", draft!.Content);
    }

    [Fact]
    public void TheSizeCap_CountsUtf8Bytes()
    {
        var store = Store(maxBytes: 10);
        Assert.Equal(DraftWriteResult.TooLarge, store.Write("ääääää", null, When)); // 6 chars, 12 bytes
    }

    [Fact]
    public void AnOversizedFile_IsNotRead()
    {
        var store = Store(maxBytes: 8);
        Directory.CreateDirectory(_root);
        File.WriteAllText(store.FilePath, "bbs-draft/1\t638668584000000000\t5000\t\n" + new string('x', 5000));
        Assert.False(store.TryLoad(out _));
    }

    [Fact]
    public void Due_AfterARest_OrAtTheLatestAfterTheMaxWait()
    {
        var store = Store();
        Assert.False(store.Due(0)); // nothing pending
        store.MarkDirty(10);
        Assert.True(store.Pending);
        Assert.False(store.Due(10 + store.QuietSeconds - 0.1));
        Assert.True(store.Due(10 + store.QuietSeconds));

        // A child who never pauses: every change within the quiet time, yet the draft comes after the max wait.
        var busy = Store("busy");
        for (double t = 0; t < busy.MaxWaitSeconds; t += 0.5)
        {
            busy.MarkDirty(t);
            Assert.False(busy.Due(t));
        }

        busy.MarkDirty(busy.MaxWaitSeconds);
        Assert.True(busy.Due(busy.MaxWaitSeconds));
    }

    [Fact]
    public void Write_ClearsPending_AndCancelPendingForgetsIt()
    {
        var store = Store();
        store.MarkDirty(1);
        store.Write("work", null, When);
        Assert.False(store.Pending);
        store.MarkDirty(2);
        store.CancelPending();
        Assert.False(store.Pending);
        Assert.False(store.Due(100));
    }

    [Fact]
    public void ADiskThatRefuses_FailsQuietly()
    {
        // The drafts "folder" is a file: creating the folder fails — no exception, the editor keeps working.
        File.WriteAllText(_root, "in the way");
        try
        {
            var store = new EditorDraftStore(Path.Combine(_root, "drafts"), "ship");
            Assert.Equal(DraftWriteResult.Failed, store.Write("work", null, When));
            Assert.False(store.HasDraft);
            Assert.False(store.TryLoad(out _));
            store.Clear();
            Assert.False(store.SetAside());
        }
        finally
        {
            File.Delete(_root);
        }
    }
}
