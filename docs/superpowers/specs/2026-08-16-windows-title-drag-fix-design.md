# Windows Title Drag Fix Design

## Goal

Make the existing 48-DIP title bar draggable from its blank area while preserving the More button and the existing window-position persistence behavior.

## Root Cause

`MainWindow.xaml` already routes `MouseLeftButtonDown` from the title-bar `Grid` to `Header_MouseLeftButtonDown`, which calls `Window.DragMove()`. The `Grid` has no background, so its blank pixels do not participate in WPF hit testing and the handler does not receive presses made in that area.

## Design

Set the title-bar `Grid` background to `Transparent`. This makes the full title-bar surface hit-testable without changing its appearance. Keep `Header_MouseLeftButtonDown`, `DragMove()`, the More button, and position persistence unchanged. Button input remains owned by the button because WPF controls handle their own mouse press before the routed title-bar handler can start a drag.

## Verification

Add an STA WPF regression test that loads `MainWindow`, locates the row-zero title-bar `Grid`, arranges it at its approved size, and verifies that a blank point hits the title bar. Observe the test fail before the XAML change and pass afterward. Then run the full Release test suite and warnings-as-errors build, rebuild the self-contained application and installer once, silently upgrade the per-user installation, and confirm one monitor process remains alive without a new application crash event.

## Scope Boundaries

- Do not make the task rows, error strip, or More button into drag surfaces.
- Do not change window dimensions, styling, placement, saved-position behavior, logging, Codex data access, or sidebar automation.
- Keep the 18 real UI, reboot, and login acceptance checks separate and unchecked until a person performs them.
