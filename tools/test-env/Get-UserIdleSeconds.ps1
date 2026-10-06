# Seconds since the last keyboard or mouse input in this Windows session. Unattended runs use it to avoid
# opening game windows while the user is at the PC.
Add-Type -Namespace Native -Name Input -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct LastInputInfo { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LastInputInfo info);
'@
$info = New-Object Native.Input+LastInputInfo
$info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
[void][Native.Input]::GetLastInputInfo([ref]$info)
[math]::Round(([uint32][Environment]::TickCount - $info.dwTime) / 1000.0)
