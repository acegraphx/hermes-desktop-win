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
Get-ChildItem $outDir -Filter "sidebar-*.png" -ErrorAction SilentlyContinue | Remove-Item -Force
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
if (-not $window) { Write-Host "ERROR: window not found"; $proc | Stop-Process -Force; exit 1 }

$hwnd = [IntPtr]$window.Current.NativeWindowHandle
[Win32.NativeWin]::SetForegroundWindow($hwnd) | Out-Null
[Win32.NativeWin]::MoveWindow($hwnd, 100, 50, 1300, 800, $true) | Out-Null
Start-Sleep -Milliseconds 500

function Capture([string]$name) {
    Start-Sleep -Milliseconds 800
    $r = $window.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $path = Join-Path $outDir "sidebar-$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  saved $path  ($([int]$r.Width)x$([int]$r.Height))"
}

# Click "Wiki" sidebar item to give us something interesting to look at
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
    } catch {
        $r = $wikiItem.Current.BoundingRectangle
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ([int]($r.X + $r.Width / 2)), ([int]($r.Y + $r.Height / 2))
        [Win32.NativeWin]::mouse_event(0x0002, 0, 0, 0, 0)
        [Win32.NativeWin]::mouse_event(0x0004, 0, 0, 0, 0)
    }
}
Start-Sleep -Seconds 2

# Find toggle button — by its tooltip "Collapse sidebar"
function Find-ToggleButton {
    $tipExpand = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::HelpTextProperty, "Expand sidebar")
    $tipCollapse = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::HelpTextProperty, "Collapse sidebar")
    $btn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tipCollapse)
    if (-not $btn) { $btn = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tipExpand) }
    return $btn
}

function Click-Element([System.Windows.Automation.AutomationElement]$el) {
    if (-not $el) { return }
    try {
        $inv = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $inv.Invoke()
        Write-Host "  invoked"
    } catch {
        $r = $el.Current.BoundingRectangle
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ([int]($r.X + $r.Width / 2)), ([int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 100
        [Win32.NativeWin]::mouse_event(0x0002, 0, 0, 0, 0)
        Start-Sleep -Milliseconds 50
        [Win32.NativeWin]::mouse_event(0x0004, 0, 0, 0, 0)
        Write-Host "  mouse-clicked"
    }
}

Capture "01-expanded"

$toggle = Find-ToggleButton
if ($toggle) {
    Write-Host "Clicking toggle (collapse)"
    Click-Element $toggle
    Start-Sleep -Seconds 1
    Capture "02-collapsed"

    $toggle = Find-ToggleButton
    Write-Host "Clicking toggle (expand again)"
    Click-Element $toggle
    Start-Sleep -Seconds 1
    Capture "03-expanded-again"
} else {
    Write-Host "ERROR: toggle button not found"
}

Start-Sleep -Seconds 1
$proc | Stop-Process -Force
Write-Host "Done. Screenshots in $outDir"
