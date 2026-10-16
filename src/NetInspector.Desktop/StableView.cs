using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace NetInspector.Desktop;

// Commit a complete update without exposing intermediate text, selection or scroll changes.
internal static class StableView
{
    private sealed class DocumentState { public string? Rtf; }
    private static readonly ConditionalWeakTable<RichTextBox, DocumentState> documents = new();

    public static void SetDocument(RichTextBox view, string? rtf)
    {
        // RichEdit normalizes RTF on assignment; compare the generated documents instead.
        var state = documents.GetOrCreateValue(view);
        if (state.Rtf == rtf) return;
        var scroll = new Point();
        var start = view.SelectionStart;
        var length = view.SelectionLength;
        var handle = view.Handle;
        SendPoint(handle, 0x4DD, IntPtr.Zero, ref scroll); // EM_GETSCROLLPOS
        SendMessage(handle, 0xB, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW
        try
        {
            view.Rtf = rtf;
            start = Math.Min(start, view.TextLength);
            view.Select(start, Math.Min(length, view.TextLength - start));
            SendPoint(handle, 0x4DE, IntPtr.Zero, ref scroll); // EM_SETSCROLLPOS
            state.Rtf = rtf;
        }
        finally
        {
            SendMessage(handle, 0xB, new IntPtr(1), IntPtr.Zero);
            view.Invalidate();
        }
    }

    public static void SetRows<T>(DataGridView view, T[] rows)
    {
        var previous = view.DataSource as T[];
        if (previous != null && previous.SequenceEqual(rows)) return;
        var top = view.FirstDisplayedScrollingRowIndex;
        var horizontal = view.HorizontalScrollingOffset;
        var row = view.CurrentCell?.RowIndex ?? -1;
        var column = view.CurrentCell?.ColumnIndex ?? -1;
        // Descending histories insert at the top: keep the record being read in place.
        int RetainedIndex(int index)
        {
            if (previous == null || index < 0 || index >= previous.Length) return index;
            var retained = Array.IndexOf(rows, previous[index]);
            return retained >= 0 ? retained : index;
        }
        top = top > 0 ? RetainedIndex(top) : top;
        row = RetainedIndex(row);
        var handle = view.Handle;
        SendMessage(handle, 0xB, IntPtr.Zero, IntPtr.Zero);
        try
        {
            view.DataSource = rows;
            if (row >= 0 && view.Rows.Count > 0 && column >= 0 && column < view.Columns.Count)
                view.CurrentCell = view[column, Math.Min(row, view.Rows.Count - 1)];
            if (top >= 0 && view.Rows.Count > 0)
                view.FirstDisplayedScrollingRowIndex = Math.Min(top, view.Rows.Count - 1);
            view.HorizontalScrollingOffset = horizontal;
        }
        finally
        {
            SendMessage(handle, 0xB, new IntPtr(1), IntPtr.Zero);
            view.Invalidate();
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendPoint(IntPtr handle, int message, IntPtr wParam, ref Point point);
}
