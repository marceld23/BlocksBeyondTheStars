// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3102: the build categories of <c>data/categories.json</c>, resolved on first use.</summary>
public sealed partial class GameContent
{
    private CategoryCatalog? _categoryCatalog;
    private ContentCategories? _categories;

    /// <summary>The raw category file as loaded (null when <c>data/categories.json</c> is absent) — for the content tests.</summary>
    public CategoryCatalog? CategoryCatalog => _categoryCatalog;

    /// <summary>Which categories every item, blueprint, ship module and ship belongs to. Resolved on first read (the
    /// server never asks — the categories are presentation only); <see cref="ContentCategories.Empty"/> without the file.</summary>
    public ContentCategories Categories => _categories ??= ContentCategories.Build(_categoryCatalog, this);

    /// <summary>Installs the category file (called by the content loader); null means no categories.</summary>
    public void SetCategories(CategoryCatalog? catalog)
    {
        _categoryCatalog = catalog;
        _categories = null;
    }
}
