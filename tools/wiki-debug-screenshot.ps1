$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -MemberDefinition '
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
' -Name "NativeWin" -Namespace "Win32"

$outDir = "C:\temp\hermes-debug"
Get-ChildItem $outDir -ErrorAction SilentlyContinue | Remove-Item -Force
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$exePath = Join-Path $PSScriptRoot "..\publish\HermesDesktop.exe" | Resolve-Path
Write-Host "Launching: $exePath"
$proc = Start-Process $exePath -PassThru
Start-Sleep -Seconds 6

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$window = $null
for ($i = 0; $i -lt 20; $i++) {
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
    if ($window) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $window) { Write-Host "ERROR: Window not found"; $proc | Stop-Process -Force; exit 1 }
Write-Host "Window: $($window.Current.Name)  hwnd=$($window.Current.NativeWindowHandle)"

$hwnd = [IntPtr]$window.Current.NativeWindowHandle
[Win32.NativeWin]::SetForegroundWindow($hwnd) | Out-Null
[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 1400, 900, $true) | Out-Null
Start-Sleep -Milliseconds 500

function Capture-Window([string]$name, [int]$pad = 0) {
    Start-Sleep -Milliseconds 1200
    $r = $window.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { return }
    $w = [int]$r.Width + $pad*2
    $h = [int]$r.Height + $pad*2
    $x = [int]$r.X - $pad
    $y = [int]$r.Y - $pad
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $path = Join-Path $outDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  saved $path  (${w}x${h} @ ${x},${y})"
}

# Click "Wiki" sidebar item by name
$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Wiki")
$candidates = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
$wikiItem = $null
foreach ($c in $candidates) {
    if ($c.Current.ControlType.LocalizedControlType -eq "list item") { $wikiItem = $c; break }
}
if (-not $wikiItem -and $candidates.Count -gt 0) { $wikiItem = $candidates[0] }
if ($wikiItem) {
    try {
        $sip = $wikiItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sip.Select()
        Write-Host "Wiki tab selected via SelectionItemPattern"
    } catch {
        $r = $wikiItem.Current.BoundingRectangle
        $cx = [int]($r.X + $r.Width / 2)
        $cy = [int]($r.Y + $r.Height / 2)
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $cx, $cy
        Start-Sleep -Milliseconds 100
        [Win32.NativeWin]::mouse_event(0x0002, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 50
        [Win32.NativeWin]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "Wiki tab clicked via mouse"
    }
}

Start-Sleep -Seconds 4
Capture-Window "10-wiki-loaded"

# Try to find the first leaf TreeViewItem (file, not directory) and click it.
# Look for a tree item whose Name is a wiki page; pick first that doesn't have children expanded.
$treeItemCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::TreeItem)
$treeItems = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeItemCond)
Write-Host "Tree items found: $($treeItems.Count)"

$leaf = $null
foreach ($ti in $treeItems) {
    # Heuristic: a leaf has no children
    $expandPat = $null
    try { $expandPat = $ti.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern) } catch {}
    $name = $ti.Current.Name
    Write-Host "  tree item: '$name'  (hasExpandPattern=$($null -ne $expandPat))"
    if ($null -eq $expandPat) { $leaf = $ti; break }
    # If has expand pattern but state is leaf (no children), use it. Otherwise expand.
    if ($expandPat.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::LeafNode) {
        $leaf = $ti; break
    }
}

if (-not $leaf -and $treeItems.Count -gt 0) {
    # Try expanding the first item then descending
    $first = $treeItems[0]
    try {
        $ep = $first.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $ep.Expand()
        Start-Sleep -Milliseconds 500
        $treeItems = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeItemCond)
        foreach ($ti in $treeItems) {
            $expandPat = $null
            try { $expandPat = $ti.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern) } catch {}
            if ($null -eq $expandPat) { $leaf = $ti; break }
            if ($expandPat.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::LeafNode) {
                $leaf = $ti; break
            }
        }
    } catch {}
}

if ($leaf) {
    Write-Host "Leaf: '$($leaf.Current.Name)'"
    try {
        $sip = $leaf.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sip.Select()
        Write-Host "  selected leaf"
    } catch {
        Write-Host "  SelectionItemPattern failed: $_"
        $r = $leaf.Current.BoundingRectangle
        $cx = [int]($r.X + $r.Width / 2)
        $cy = [int]($r.Y + $r.Height / 2)
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point $cx, $cy
        Start-Sleep -Milliseconds 100
        [Win32.NativeWin]::mouse_event(0x0002, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 50
        [Win32.NativeWin]::mouse_event(0x0004, 0, 0, 0, 0)
    }
} else {
    Write-Host "No leaf tree item found"
}

Start-Sleep -Seconds 5
Capture-Window "11-page-opened"

# Resize via MoveWindow to test WebView2 sticking-out bug.
# Take padded screenshots so we can see if WebView2 leaks past borders.
Write-Host "Resize sequence to test WebView2"

[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 900, 700, $true) | Out-Null
Capture-Window "12-resized-900x700" 30

[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 1500, 900, $true) | Out-Null
Capture-Window "13-resized-1500x900" 30

[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 800, 600, $true) | Out-Null
Capture-Window "14-resized-800x600" 30

# Maximize
$wp = $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
$wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
Start-Sleep -Milliseconds 1500
Capture-Window "15-maximized" 0

# Restore
$wp.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
Start-Sleep -Milliseconds 800
[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 1200, 800, $true) | Out-Null
Capture-Window "16-restored-1200x800" 30

Start-Sleep -Seconds 1
Write-Host "Closing app"
$proc | Stop-Process -Force
Write-Host "Done. Screenshots in $outDir"
