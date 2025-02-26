﻿using System.Collections.Generic;
using OpenToolkit.GraphicsLibraryFramework;
using Robust.Shared.Utility;

namespace Robust.Client.Graphics.Clyde
{
    internal partial class Clyde
    {
        private sealed unsafe partial class GlfwWindowingImpl
        {
            // TODO: GLFW doesn't have any events for complex monitor config changes,
            // so we need some way to reload stuff if e.g. the primary monitor changes.
            // Still better than SDL2 though which doesn't acknowledge monitor changes at all.

            // Monitors are created at GLFW's will,
            // so we need to make SURE monitors keep existing while operating on them.
            // because, you know, async. Don't want a use-after-free.
            private readonly Dictionary<int, WinThreadMonitorReg> _winThreadMonitors = new();

            // Can't use ClydeHandle because it's 64 bit.
            // TODO: this should be MONITOR ID.
            private int _nextMonitorId = 1;
            private int _primaryMonitorId;
            private readonly Dictionary<int, GlfwMonitorReg> _monitors = new();

            private void InitMonitors()
            {
                var monitors = GLFW.GetMonitorsRaw(out var count);

                for (var i = 0; i < count; i++)
                {
                    WinThreadSetupMonitor(monitors[i]);
                }

                var primaryMonitor = GLFW.GetPrimaryMonitor();
                var up = GLFW.GetMonitorUserPointer(primaryMonitor);
                _primaryMonitorId = (int) up;

                ProcessEvents();
            }

            private void WinThreadSetupMonitor(Monitor* monitor)
            {
                var id = _nextMonitorId++;

                DebugTools.Assert(GLFW.GetMonitorUserPointer(monitor) == null,
                    "GLFW window already has user pointer??");

                var name = GLFW.GetMonitorName(monitor);
                var videoMode = GLFW.GetVideoMode(monitor);

                GLFW.SetMonitorUserPointer(monitor, (void*) id);

                _winThreadMonitors.Add(id, new WinThreadMonitorReg {Ptr = monitor});

                SendEvent(new EventMonitorSetup(id, name, *videoMode));
            }

            private void ProcessSetupMonitor(EventMonitorSetup ev)
            {
                var impl = new MonitorHandle(
                    ev.Id,
                    ev.Name,
                    (ev.Mode.Width, ev.Mode.Height),
                    ev.Mode.RefreshRate);

                _clyde._monitorHandles.Add(impl);
                _monitors[ev.Id] = new GlfwMonitorReg
                {
                    Id = ev.Id,
                    Handle = impl
                };
            }

            private void WinThreadDestroyMonitor(Monitor* monitor)
            {
                var ptr = (int) GLFW.GetMonitorUserPointer(monitor);

                if (ptr == 0)
                {
                    var name = GLFW.GetMonitorName(monitor);
                    _sawmill.Warning($"Monitor '{name}' had no user pointer set??");
                    return;
                }

                _winThreadMonitors.Remove(ptr);

                GLFW.SetMonitorUserPointer(monitor, null);

                SendEvent(new EventMonitorDestroy(ptr));
            }

            private void ProcessEventDestroyMonitor(EventMonitorDestroy ev)
            {
                var reg = _monitors[ev.Id];
                _monitors.Remove(ev.Id);
                _clyde._monitorHandles.Remove(reg.Handle);
            }

            private sealed class GlfwMonitorReg : MonitorReg
            {
                public int Id;
            }

            private sealed class WinThreadMonitorReg
            {
                public Monitor* Ptr;
            }
        }
    }
}
