using System.Numerics;

namespace JipperKeyViewer.Mobile;

internal readonly record struct KeyRect(int Index, bool Foot, int Row, Vector2 Min, Vector2 Max)
{
    internal bool Contains(float x, float y)
        => x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y;
}

internal readonly record struct StatusRect(bool Total, Vector2 Min, Vector2 Max);

internal static class KeyViewerLayout
{
    private static readonly int[] Empty = Array.Empty<int>();
    private static readonly int[] Back10 = { 8, 9 };
    private static readonly int[] Back12 = { 9, 8, 10, 11 };
    private static readonly int[] Back14 = { 13, 9, 8, 10, 11, 12 };
    private static readonly int[] Back16 = { 12, 13, 9, 8, 10, 11, 14, 15 };
    private static readonly int[] Back20 = { 12, 13, 9, 8, 10, 11, 14, 15 };
    private static readonly int[] Back24 = { 12, 13, 9, 8, 10, 11, 14, 15 };
    private static readonly int[] Third20 = { 17, 16, 18, 19 };
    private static readonly int[] Third24 = { 17, 16, 18, 19, 21, 20, 22, 23 };

    internal static void Build(
        KeyViewerSettings settings,
        Vector2 display,
        List<KeyRect> target,
        List<StatusRect> statusTarget,
        out Vector2 boundsMin,
        out Vector2 boundsMax)
    {
        target.Clear();
        statusTarget.Clear();
        int mainCount = Defaults.Count(settings.Layout);
        int footCount = Math.Clamp(settings.FootKeyCount, 0, 16);
        int mainRows = MainRows(settings.Layout);
        bool showStatus = !settings.StreamerMode && (settings.ShowKps || settings.ShowTotal);
        int statusRows = showStatus ? 1 : 0;
        bool betweenStatus = footCount > 0
            && footCount <= 8
            && settings.FootPlacement == FootKeyPlacement.BetweenKpsAndTotal
            && showStatus
            && settings.ShowKps
            && settings.ShowTotal;
        bool customFoot = footCount > 0 && settings.FootPlacement == FootKeyPlacement.Custom;
        int footRows = FootRows(footCount);
        int rowCount = mainRows + statusRows
            + (footCount > 0 && !betweenStatus && !customFoot ? footRows : 0);

        float scale = Math.Clamp(settings.Scale, 0.45f, 2f);
        float keyWidth = Math.Clamp(settings.KeyWidth, 24f, 160f) * scale;
        float keyHeight = Math.Clamp(settings.KeyHeight, 24f, 160f) * scale;
        float gap = Math.Clamp(settings.KeyGap, 1f, 12f) * scale;
        float keyboardWidth = keyWidth * 8f + gap * 7f;
        float availableWidth = Math.Max(160f, display.X - 16f);
        if (keyboardWidth > availableWidth)
        {
            float factor = availableWidth / keyboardWidth;
            keyWidth *= factor;
            keyHeight *= factor;
            gap *= factor;
            keyboardWidth = availableWidth;
        }

        float totalHeight = rowCount * keyHeight + Math.Max(0, rowCount - 1) * gap;
        // PositionY keeps the old bottom-margin direction for existing settings:
        // 0 is the bottom edge and 1 is the top edge. The full range is clamped
        // only enough to keep the complete keyboard visible on screen.
        float bottom = display.Y * (1f - Math.Clamp(settings.PositionY, 0f, 1f));
        if (display.Y > totalHeight + 8f)
            bottom = Math.Clamp(bottom, totalHeight + 4f, display.Y - 4f);
        else
            bottom = display.Y - 4f;

        float centerX = display.X * Math.Clamp(settings.PositionX, 0f, 1f);
        float left = Math.Clamp(centerX - keyboardWidth * 0.5f, 8f, Math.Max(8f, display.X - keyboardWidth - 8f));
        boundsMin = new Vector2(left, bottom - totalHeight);
        boundsMax = new Vector2(left + keyboardWidth, bottom);

        if (showStatus)
        {
            if (betweenStatus)
                AddBetweenStatusRow(
                    target,
                    statusTarget,
                    footCount,
                    left,
                    bottom,
                    keyboardWidth,
                    keyWidth,
                    keyHeight,
                    gap);
            else
                AddStatusRow(
                    statusTarget,
                    settings.ShowKps,
                    settings.ShowTotal,
                    left,
                    bottom,
                    keyboardWidth,
                    keyHeight,
                    gap);
        }

        if (footCount > 0 && !betweenStatus)
        {
            if (customFoot)
                AddCustomFootRows(target, settings, footCount, display, keyWidth, keyHeight, gap);
            else
                AddFootRows(target, footCount, statusRows, left, bottom, keyboardWidth, keyWidth, keyHeight, gap);
        }

        int mainOffset = statusRows + (footCount > 0 && !betweenStatus && !customFoot ? footRows : 0);
        AddSequentialRow(
            target,
            0,
            Math.Min(8, mainCount),
            false,
            mainOffset,
            left,
            bottom,
            keyboardWidth,
            keyWidth,
            keyHeight,
            gap);

        if (mainRows >= 2)
        {
            int[] back = BackSequence(settings.Layout);
            AddRow(target, back, back.Length, false, mainOffset + 1, left, bottom, keyboardWidth, keyWidth, keyHeight, gap);
        }

        if (mainRows >= 3)
        {
            int[] third = settings.Layout == KeyLayout.Key24 ? Third24 : Third20;
            AddRow(target, third, third.Length, false, mainOffset + 2, left, bottom, keyboardWidth, keyWidth, keyHeight, gap);
        }

        if (target.Count > 0)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (KeyRect rect in target)
            {
                minX = Math.Min(minX, rect.Min.X);
                minY = Math.Min(minY, rect.Min.Y);
                maxX = Math.Max(maxX, rect.Max.X);
                maxY = Math.Max(maxY, rect.Max.Y);
            }
            boundsMin = new Vector2(Math.Min(boundsMin.X, minX), Math.Min(boundsMin.Y, minY));
            boundsMax = new Vector2(Math.Max(boundsMax.X, maxX), Math.Max(boundsMax.Y, maxY));
        }
    }

    private static void AddStatusRow(
        List<StatusRect> target,
        bool showKps,
        bool showTotal,
        float left,
        float bottom,
        float keyboardWidth,
        float keyHeight,
        float gap)
    {
        float y = bottom - keyHeight;
        if (showKps && showTotal)
        {
            float width = (keyboardWidth - gap) * 0.5f;
            target.Add(new StatusRect(false, new Vector2(left, y), new Vector2(left + width, bottom)));
            float totalLeft = left + width + gap;
            target.Add(new StatusRect(true, new Vector2(totalLeft, y), new Vector2(left + keyboardWidth, bottom)));
            return;
        }

        target.Add(new StatusRect(
            showTotal,
            new Vector2(left, y),
            new Vector2(left + keyboardWidth, bottom)));
    }

    private static void AddBetweenStatusRow(
        List<KeyRect> keyTarget,
        List<StatusRect> statusTarget,
        int footCount,
        float left,
        float bottom,
        float keyboardWidth,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        float footKeyWidth = keyWidth;
        float footWidth = footCount * footKeyWidth + Math.Max(0, footCount - 1) * gap;
        float minimumStatusWidth = Math.Clamp(keyHeight * 1.1f, 48f, 120f);
        float availableFootWidth = keyboardWidth - minimumStatusWidth * 2f - gap * 2f;
        if (footWidth > availableFootWidth && availableFootWidth > 0f)
        {
            footKeyWidth = Math.Max(8f, (availableFootWidth - Math.Max(0, footCount - 1) * gap) / footCount);
            footWidth = footCount * footKeyWidth + Math.Max(0, footCount - 1) * gap;
        }

        float sideWidth = Math.Max(16f, (keyboardWidth - footWidth - gap * 2f) * 0.5f);
        float rowTop = bottom - keyHeight;
        float footLeft = left + (keyboardWidth - footWidth) * 0.5f;
        statusTarget.Add(new StatusRect(
            false,
            new Vector2(left, rowTop),
            new Vector2(left + sideWidth, bottom)));
        statusTarget.Add(new StatusRect(
            true,
            new Vector2(left + keyboardWidth - sideWidth, rowTop),
            new Vector2(left + keyboardWidth, bottom)));
        AddHorizontalFootRow(
            keyTarget,
            0,
            footCount,
            rowTop,
            footLeft,
            footKeyWidth,
            keyHeight,
            gap);
    }

    private static void AddFootRows(
        List<KeyRect> target,
        int count,
        int firstRow,
        float left,
        float bottom,
        float keyboardWidth,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        int firstCount = Math.Min(8, count);
        AddSequentialRow(
            target,
            0,
            firstCount,
            true,
            firstRow,
            left,
            bottom,
            keyboardWidth,
            keyWidth,
            keyHeight,
            gap);
        if (count <= 8) return;

        int secondCount = count - firstCount;
        AddSequentialRow(
            target,
            firstCount,
            secondCount,
            true,
            firstRow + 1,
            left,
            bottom,
            keyboardWidth,
            keyWidth,
            keyHeight,
            gap);
    }

    private static void AddCustomFootRows(
        List<KeyRect> target,
        KeyViewerSettings settings,
        int count,
        Vector2 display,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        int firstCount = Math.Min(8, count);
        int secondCount = Math.Max(0, count - firstCount);
        int rows = secondCount > 0 ? 2 : 1;
        float firstWidth = firstCount * keyWidth + Math.Max(0, firstCount - 1) * gap;
        float secondWidth = secondCount * keyWidth + Math.Max(0, secondCount - 1) * gap;
        float width = Math.Max(firstWidth, secondWidth);
        float height = rows * keyHeight + Math.Max(0, rows - 1) * gap;
        float centerX = display.X * Math.Clamp(settings.FootPositionX, 0f, 1f);
        float centerY = display.Y * Math.Clamp(settings.FootPositionY, 0f, 1f);
        float left = Math.Clamp(centerX - width * 0.5f, 4f, Math.Max(4f, display.X - width - 4f));
        float top = Math.Clamp(centerY - height * 0.5f, 4f, Math.Max(4f, display.Y - height - 4f));
        AddHorizontalFootRow(target, 0, firstCount, top, left, keyWidth, keyHeight, gap);
        if (secondCount > 0)
        {
            float secondLeft = left + (width - secondWidth) * 0.5f;
            AddHorizontalFootRow(
                target,
                firstCount,
                secondCount,
                top + keyHeight + gap,
                secondLeft,
                keyWidth,
                keyHeight,
                gap);
        }
    }

    private static void AddSequentialRow(
        List<KeyRect> target,
        int indexStart,
        int count,
        bool foot,
        int row,
        float left,
        float bottom,
        float keyboardWidth,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        if (count <= 0) return;
        float rowWidth = count * keyWidth + (count - 1) * gap;
        float rowLeft = left + (keyboardWidth - rowWidth) * 0.5f;
        float y = bottom - (row + 1) * keyHeight - row * gap;
        AddHorizontalFootRow(target, indexStart, count, y, rowLeft, keyWidth, keyHeight, gap, row, foot);
    }

    private static void AddHorizontalFootRow(
        List<KeyRect> target,
        int indexStart,
        int count,
        float y,
        float rowLeft,
        float keyWidth,
        float keyHeight,
        float gap,
        int row = 0,
        bool foot = true)
    {
        for (int i = 0; i < count; i++)
        {
            int index = indexStart + i;
            float x = rowLeft + i * (keyWidth + gap);
            target.Add(new KeyRect(
                index,
                foot,
                row,
                new Vector2(x, y),
                new Vector2(x + keyWidth, y + keyHeight)));
        }
    }

    private static int FootRows(int count) => count > 8 ? 2 : count > 0 ? 1 : 0;

    internal static int[] BackSequence(KeyLayout layout) => layout switch
    {
        KeyLayout.Key10 => Back10,
        KeyLayout.Key12 => Back12,
        KeyLayout.Key14 => Back14,
        KeyLayout.Key16 => Back16,
        KeyLayout.Key20 => Back20,
        KeyLayout.Key24 => Back24,
        _ => Empty,
    };

    internal static int[] ThirdSequence(KeyLayout layout) => layout switch
    {
        KeyLayout.Key20 => Third20,
        KeyLayout.Key24 => Third24,
        _ => Empty,
    };

    internal static int MainRows(KeyLayout layout)
        => Defaults.Count(layout) >= 20 ? 3 : Defaults.Count(layout) > 8 ? 2 : 1;

    private static void AddRow(
        List<KeyRect> target,
        IReadOnlyList<string> bindings,
        int count,
        bool foot,
        int row,
        float left,
        float bottom,
        float keyboardWidth,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        var indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        AddRow(target, indices, count, foot, row, left, bottom, keyboardWidth, keyWidth, keyHeight, gap);
    }

    private static void AddRow(
        List<KeyRect> target,
        IReadOnlyList<int> indices,
        int count,
        bool foot,
        int row,
        float left,
        float bottom,
        float keyboardWidth,
        float keyWidth,
        float keyHeight,
        float gap)
    {
        if (count <= 0) return;
        float rowWidth = count * keyWidth + (count - 1) * gap;
        float rowLeft = left + (keyboardWidth - rowWidth) * 0.5f;
        float y = bottom - (row + 1) * keyHeight - row * gap;
        for (int i = 0; i < count; i++)
        {
            float x = rowLeft + i * (keyWidth + gap);
            target.Add(new KeyRect(
                indices[i],
                foot,
                row,
                new Vector2(x, y),
                new Vector2(x + keyWidth, y + keyHeight)));
        }
    }
}
