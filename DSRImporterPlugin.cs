#if TOOLS
using Godot;

namespace DSRImporter
{
    [Tool]
    public partial class DSRImporterPlugin : EditorPlugin
    {
        private DSRImporterDock _dock;

        public override void _EnterTree()
        {
            _dock = new DSRImporterDock();
            AddControlToDock(DockSlot.RightUl, _dock);
        }

        public override void _ExitTree()
        {
            if (_dock != null)
            {
                RemoveControlFromDocks(_dock);
                _dock.QueueFree();
            }
        }
    }
}
#endif
