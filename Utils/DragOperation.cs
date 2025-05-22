using RightClickAppLauncher.Models;

namespace RightClickAppLauncher.Utils;

public class DragOperation
{
    public Guid ItemId { get; }
    public double PreviousX { get; }
    public double PreviousY { get; }
    public double CurrentX { get; }
    public double CurrentY { get; }

    public DragOperation(Guid itemId, double prevX, double prevY, double currentX, double currentY)
    {
        ItemId = itemId;
        PreviousX = prevX;
        PreviousY = prevY;
        CurrentX = currentX;
        CurrentY = currentY;
    }
}

public class DragHistoryManager
{
    readonly Stack<DragOperation> _undoStack = new Stack<DragOperation>();
    readonly Stack<DragOperation> _redoStack = new Stack<DragOperation>();
    readonly Action<LauncherItem, double, double> _applyPositionAction;

    public DragHistoryManager(Action<LauncherItem, double, double> applyPositionAction) => _applyPositionAction = applyPositionAction ?? throw new ArgumentNullException(nameof(applyPositionAction));

    public void RecordDrag(LauncherItem item, double oldX, double oldY)
    {
        _undoStack.Push(new DragOperation(item.Id, oldX, oldY, item.X, item.Y));
        _redoStack.Clear();
    }

    public void RecordDrag(LauncherItem item, double oldX, double oldY, double itemX, double itemY)
    {
        _undoStack.Push(new DragOperation(item.Id, oldX, oldY, itemX, itemY));
        _redoStack.Clear();
    }

    public bool CanUndo => _undoStack.Any();
    public bool CanRedo => _redoStack.Any();

    public void Undo(Func<Guid, LauncherItem> findItemById)
    {
        if(!CanUndo) return;

        var lastOp = _undoStack.Pop();
        var item = findItemById(lastOp.ItemId);
        if(item != null)
        {
            _applyPositionAction(item, lastOp.PreviousX, lastOp.PreviousY);
            _redoStack.Push(new DragOperation(item.Id, lastOp.PreviousX, lastOp.PreviousY, item.X, item.Y));
        }
    }

    public void Redo(Func<Guid, LauncherItem> findItemById)
    {
        if(!CanRedo) return;

        var nextOp = _redoStack.Pop();
        var item = findItemById(nextOp.ItemId);
        if(item != null)
        {
            _applyPositionAction(item, nextOp.CurrentX, nextOp.CurrentY);
            _undoStack.Push(new DragOperation(item.Id, item.X, item.Y, nextOp.CurrentX, nextOp.CurrentY));
        }
    }

    public void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}