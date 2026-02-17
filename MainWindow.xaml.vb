Imports System.IO
Imports System.Diagnostics
Imports Microsoft.Win32
Imports System.Management
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading.Tasks
Imports System.Windows.Threading
Imports System.Windows.Navigation
Imports System.Net
Imports System.Net.Http
Imports System.Reflection

Class MainWindow
    Inherits Window

    ' ==================================================================================
    ' KONFIGURASI AUTO UPDATE & SYSTEM
    ' ==================================================================================
    ' Ganti URL ini dengan link raw file .txt versi Anda
    Private Const UpdateInfoUrl As String = "https://raw.githubusercontent.com/Din2312/Roblox-Diskless-Launcher/main/version.txt"
    Private CurrentVersion As Version = Assembly.GetExecutingAssembly().GetName().Version

    ' ==================================================================================
    ' BAGIAN 1: STRUKTUR FOLDER & PATH
    ' ==================================================================================
#Region "📁 Roblox (Root Folder Portable)"
    Private ReadOnly appPath As String = AppDomain.CurrentDomain.BaseDirectory
    Private ReadOnly datPath As String = Path.Combine(appPath, "cakung_sys.dat")
    Private ReadOnly trialDataPath As String = Path.Combine(appPath, "win_time_config.sys")
    Private ReadOnly eulaPath As String = Path.Combine(appPath, "rbx_eula.dat")
    Private Const SecretSalt As String = "ROBLOX-NEON-SECRET-2026"
#End Region

#Region "📁 local (Folder Data User/Cache)"
    Private ReadOnly serverLocalData As String = Path.Combine(appPath, "local", "Roblox")
    Private ReadOnly localRobloxPath As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox")
#End Region

#Region "📁 Versions (Folder Game Engine)"
    Private ReadOnly serverVersions As String = Path.Combine(appPath, "Versions")
    Private ReadOnly prog86Roblox As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox")
#End Region

    ' ==================================================================================
    ' BAGIAN 2: SYSTEM STARTUP
    ' ==================================================================================
    Private Async Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        ExecuteBypass(False)

        ' Cek Update
        Await CheckForUpdates()

        ' Buat Folder
        If Not Directory.Exists(serverVersions) Then
            Directory.CreateDirectory(serverVersions)
        End If
        If Not Directory.Exists(serverLocalData) Then
            Directory.CreateDirectory(serverLocalData)
        End If

        ' Pasang Junction
        Await Task.Run(Sub() ForceUpdateJunctions())

        ' UI Logic
        If MainBorder IsNot Nothing Then MainBorder.Visibility = Visibility.Collapsed
        If EulaOverlay IsNot Nothing Then EulaOverlay.Visibility = Visibility.Collapsed
        If SplashGrid IsNot Nothing Then SplashGrid.Visibility = Visibility.Visible

        ' Simulasi Loading
        TrialTimer.Interval = TimeSpan.FromSeconds(1)
        Await Task.Delay(3000)

        If SplashGrid IsNot Nothing Then SplashGrid.Visibility = Visibility.Collapsed

        ' Cek EULA
        If Not System.IO.File.Exists(eulaPath) Then
            If EulaOverlay IsNot Nothing Then EulaOverlay.Visibility = Visibility.Visible
        Else
            EnterMainMenu()
        End If
    End Sub

    Private Sub EnterMainMenu()
        If EulaOverlay IsNot Nothing Then EulaOverlay.Visibility = Visibility.Collapsed
        If MainBorder IsNot Nothing Then MainBorder.Visibility = Visibility.Visible
        CheckLicenseStatus()
    End Sub

    ' ==================================================================================
    ' BAGIAN 3: AUTO UPDATE SYSTEM
    ' ==================================================================================
    Private Async Function CheckForUpdates() As Task
        Try
            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Checking for updates..."

            Using client As New HttpClient()
                client.Timeout = TimeSpan.FromSeconds(5)
                Dim response = Await client.GetStringAsync(UpdateInfoUrl)
                Dim parts = response.Trim().Split("|"c)

                If parts.Length >= 2 Then
                    Dim remoteVersion As New Version(parts(0))
                    Dim downloadUrl As String = parts(1)

                    If remoteVersion > CurrentVersion Then
                        Dim result = MessageBox.Show($"Versi Baru Tersedia: {remoteVersion}" & vbCrLf & "Update sekarang?", "Auto Update", MessageBoxButton.YesNo, MessageBoxImage.Information)
                        If result = MessageBoxResult.Yes Then
                            Await PerformUpdate(downloadUrl)
                        End If
                    End If
                End If
            End Using
        Catch ex As Exception
            Debug.WriteLine("Update check failed: " & ex.Message)
        End Try
    End Function

    Private Async Function PerformUpdate(url As String) As Task
        Try
            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Downloading Update..."
            If PrgBar IsNot Nothing Then
                PrgBar.Visibility = Visibility.Visible
                PrgBar.IsIndeterminate = True
            End If

            Dim currentExe As String = Process.GetCurrentProcess().MainModule.FileName
            Dim newExe As String = currentExe & ".new"
            Dim batchScript As String = Path.Combine(appPath, "updater.bat")

            Using client As New HttpClient()
                Dim data = Await client.GetByteArrayAsync(url)
                File.WriteAllBytes(newExe, data)
            End Using

            Dim scriptContent As String = "@echo off" & vbCrLf &
                                          "timeout /t 2 /nobreak >nul" & vbCrLf &
                                          "del """ & currentExe & """" & vbCrLf &
                                          "move """ & newExe & """ """ & currentExe & """" & vbCrLf &
                                          "start """" """ & currentExe & """" & vbCrLf &
                                          "del ""%~f0"""

            File.WriteAllText(batchScript, scriptContent)

            Dim psi As New ProcessStartInfo(batchScript) With {
                .CreateNoWindow = True,
                .UseShellExecute = False
            }
            Process.Start(psi)
            Application.Current.Shutdown()

        Catch ex As Exception
            MessageBox.Show("Gagal Update: " & ex.Message)
            If PrgBar IsNot Nothing Then PrgBar.Visibility = Visibility.Collapsed
        End Try
    End Function

    ' ==================================================================================
    ' BAGIAN 4: GAME LAUNCHER & LOGIC
    ' ==================================================================================
    Private Async Sub BtnPlay_Click(sender As Object, e As RoutedEventArgs)
        If Not IsActivated AndAlso TrialSecondsRemaining <= 0 Then
            MessageBox.Show("Masa Trial Habis! Silakan Aktivasi.")
            Return
        End If

        BtnPlay.IsEnabled = False
        If TxtStatus IsNot Nothing Then TxtStatus.Text = "Cleaning Process..."
        If PrgBar IsNot Nothing Then
            PrgBar.Visibility = Visibility.Visible
            PrgBar.IsIndeterminate = True
        End If

        ExecuteBypass(True)
        Await Task.Run(Sub() KillRobloxBrutal())

        Try
            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Syncing Files..."
            Await Task.Run(Sub() ForceUpdateJunctions())

            Dim gamePath As String = ""
            If Directory.Exists(serverVersions) Then
                Dim files = Directory.GetFiles(serverVersions, "RobloxPlayerBeta.exe", SearchOption.AllDirectories)
                If files.Length > 0 Then
                    gamePath = files.Select(Function(f) New FileInfo(f)).OrderByDescending(Function(fi) fi.LastWriteTime).First().FullName
                End If
            End If

            If String.IsNullOrEmpty(gamePath) Then
                If PrgBar IsNot Nothing Then PrgBar.Visibility = Visibility.Collapsed
                MessageBox.Show("File Game (RobloxPlayerBeta.exe) tidak ditemukan!" & vbCrLf & "Solusi: Copy folder 'Versions' dari komputer lain ke folder aplikasi ini.", "File Missing")
                BtnPlay.IsEnabled = True
                If TxtStatus IsNot Nothing Then TxtStatus.Text = "Ready to Launch..."
                Return
            End If

            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Launching Roblox..."
            Me.Hide()

            Dim vFolder = Path.GetDirectoryName(gamePath)
            InjectRobloxRegistry(New DirectoryInfo(vFolder).Name, vFolder)

            Dim pLaunch As New ProcessStartInfo(gamePath) With {
                .Arguments = "--app",
                .WorkingDirectory = vFolder,
                .UseShellExecute = False
            }

            Dim proc = Process.Start(pLaunch)
            If proc IsNot Nothing Then
                Await Task.Run(Sub()
                                   Try
                                       proc.WaitForExit()
                                   Catch
                                   End Try
                               End Sub)
            Else
                Await Task.Delay(5000)
            End If

            Me.Show()
            Me.WindowState = WindowState.Normal

            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Ready to Launch..."
            If PrgBar IsNot Nothing Then PrgBar.Visibility = Visibility.Collapsed

        Catch ex As Exception
            MessageBox.Show("Launch Error: " & ex.Message)
            Me.Show()
            If PrgBar IsNot Nothing Then PrgBar.Visibility = Visibility.Collapsed
        Finally
            ExecuteBypass(False)
            BtnPlay.IsEnabled = True
        End Try
    End Sub

    ' ==================================================================================
    ' BAGIAN 5: SYSTEM TOOLS
    ' ==================================================================================
    Private Function ForceUpdateJunctions() As Boolean
        Try
            ResetAndLink(localRobloxPath, serverLocalData)
            ResetAndLink(prog86Roblox, serverVersions)
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub ResetAndLink(targetPath As String, sourcePath As String)
        Try
            Dim sourceHasFiles As Boolean = Directory.Exists(sourcePath) AndAlso Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories).Length > 0
            Dim targetHasFiles As Boolean = Directory.Exists(targetPath) AndAlso Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories).Length > 0

            Dim isTargetJunction As Boolean = False
            If Directory.Exists(targetPath) Then
                Dim dInfo As New DirectoryInfo(targetPath)
                If (dInfo.Attributes And FileAttributes.ReparsePoint) = FileAttributes.ReparsePoint Then
                    isTargetJunction = True
                End If
            End If

            If Not sourceHasFiles AndAlso targetHasFiles AndAlso Not isTargetJunction Then
                Try
                    If Directory.Exists(sourcePath) Then Directory.Delete(sourcePath, True)
                    Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(targetPath, sourcePath, True)
                    sourceHasFiles = True
                Catch
                    Return
                End Try
            End If

            If Directory.Exists(sourcePath) AndAlso Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories).Length > 0 Then
                If Directory.Exists(targetPath) Then
                    If isTargetJunction Then
                        Directory.Delete(targetPath)
                    Else
                        Directory.Delete(targetPath, True)
                    End If
                End If

                Dim parent = Path.GetDirectoryName(targetPath)
                If Not Directory.Exists(parent) Then Directory.CreateDirectory(parent)

                Dim cmd = "/c mklink /j """ & targetPath & """ """ & sourcePath & """"
                Dim psi As New ProcessStartInfo("cmd.exe", cmd) With {
                    .WindowStyle = ProcessWindowStyle.Hidden, .CreateNoWindow = True, .UseShellExecute = True, .Verb = "runas"
                }
                Process.Start(psi).WaitForExit()
            End If
        Catch
        End Try
    End Sub

    Private Sub KillRobloxBrutal()
        Try
            Dim psi As New ProcessStartInfo("taskkill", "/F /IM RobloxPlayerBeta.exe /T") With {
                .CreateNoWindow = True, .UseShellExecute = False
            }
            Process.Start(psi).WaitForExit()
        Catch
        End Try
    End Sub

    Private Sub ExecuteBypass(activate As Boolean)
        Dim sys32 = Environment.GetEnvironmentVariable("systemroot") & "\system32\"
        Dim prog86Path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        Dim targets = {sys32 & "HintHK.dll", sys32 & "prochook.dll", prog86Path & "\GBillingClient\x64\prochook.dll"}
        Try
            For Each f In targets
                If activate Then
                    If System.IO.File.Exists(f) Then
                        If System.IO.File.Exists(f & ".old") Then
                            System.IO.File.Delete(f & ".old")
                        End If
                        Try
                            System.IO.File.Move(f, f & ".old")
                        Catch
                        End Try
                    End If
                Else
                    If System.IO.File.Exists(f & ".old") Then
                        Try
                            If System.IO.File.Exists(f) Then
                                System.IO.File.Delete(f)
                            End If
                            System.IO.File.Move(f & ".old", f)
                        Catch
                        End Try
                    End If
                End If
            Next
        Catch
        End Try
    End Sub

    Private Sub InjectRobloxRegistry(vHash As String, fPath As String)
        Try
            Using key As RegistryKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\ROBLOX Corporation\Environments\roblox-player")
                key.SetValue("", fPath)
                key.SetValue("version", vHash)
                key.SetValue("LaunchExp", "InApp")
            End Using
        Catch
        End Try
    End Sub

    ' ==================================================================================
    ' BAGIAN 6: UI, LICENSE & TOOLS
    ' ==================================================================================
    Private IsActivated As Boolean = False
    Private WithEvents TrialTimer As New DispatcherTimer()
    Private TrialSecondsRemaining As Double = 0

    Private Sub CheckLicenseStatus()
        Try
            If System.IO.File.Exists(datPath) Then
                Dim content = System.IO.File.ReadAllText(datPath).Trim().Split("|"c)
                If content.Length = 2 AndAlso content(0) = GenerateSecureHash(GetHardwareID(), content(1)) Then
                    Dim expDate As DateTime = DateTime.ParseExact(content(1), "yyyyMMdd", Nothing)
                    If DateTime.Now <= expDate Then
                        ApplyPermanentStatus(expDate)
                        Return
                    End If
                End If
            End If

            Dim deadline As DateTime
            If Not System.IO.File.Exists(trialDataPath) Then
                deadline = DateTime.Now.AddHours(24)
                System.IO.File.WriteAllText(trialDataPath, deadline.Ticks.ToString())
                System.IO.File.SetAttributes(trialDataPath, FileAttributes.Hidden Or FileAttributes.System)
            Else
                deadline = New DateTime(Long.Parse(System.IO.File.ReadAllText(trialDataPath)))
            End If
            TrialSecondsRemaining = Math.Max(0, (deadline - DateTime.Now).TotalSeconds)
            ApplyTrialStatus()
        Catch
            TrialSecondsRemaining = 0
            ApplyTrialStatus()
        End Try
    End Sub

    Private Sub TrialTimer_Tick(sender As Object, e As EventArgs) Handles TrialTimer.Tick
        If TrialSecondsRemaining > 0 Then
            TrialSecondsRemaining -= 1
            If Not IsActivated AndAlso TxtLicenseStatus IsNot Nothing Then
                Dim t = TimeSpan.FromSeconds(TrialSecondsRemaining)
                TxtLicenseStatus.Text = String.Format("TRIAL: {0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds)
            End If
        Else
            If BtnPlay IsNot Nothing Then BtnPlay.IsEnabled = False
            If TxtLicenseStatus IsNot Nothing Then TxtLicenseStatus.Text = "TRIAL ENDED"
        End If
    End Sub

    Private Sub ApplyPermanentStatus(exp As DateTime)
        IsActivated = True
        TrialTimer.Stop()
        If TxtLicenseStatus IsNot Nothing Then
            TxtLicenseStatus.Text = "ACTIVE - " & (exp - DateTime.Now).Days & " DAYS LEFT"
            TxtLicenseStatus.Foreground = New SolidColorBrush(Color.FromRgb(0, 255, 0))
        End If
        If BtnPlay IsNot Nothing Then
            BtnPlay.IsEnabled = True
            BtnPlay.Opacity = 1
        End If
    End Sub

    Private Sub ApplyTrialStatus()
        IsActivated = False
        If TrialSecondsRemaining > 0 Then
            TrialTimer.Start()
            If TxtLicenseStatus IsNot Nothing Then TxtLicenseStatus.Foreground = New SolidColorBrush(Color.FromRgb(255, 165, 0))
            If BtnPlay IsNot Nothing Then
                BtnPlay.IsEnabled = True
                BtnPlay.Opacity = 1
            End If
        Else
            If TxtLicenseStatus IsNot Nothing Then
                TxtLicenseStatus.Text = "TRIAL ENDED"
                TxtLicenseStatus.Foreground = New SolidColorBrush(Color.FromRgb(255, 0, 0))
            End If
            If BtnPlay IsNot Nothing Then
                BtnPlay.IsEnabled = False
                BtnPlay.Opacity = 0.5
            End If
        End If
    End Sub

    Private Function GenerateSecureHash(hwid As String, dateStr As String) As String
        Using sha As SHA256 = SHA256.Create()
            Dim hash = sha.ComputeHash(Encoding.UTF8.GetBytes(hwid & dateStr & SecretSalt))
            Dim hex = BitConverter.ToString(hash).Replace("-", "").Substring(0, 12)
            Return String.Format("{0}-{1}-{2}", hex.Substring(0, 4), hex.Substring(4, 4), hex.Substring(8, 4))
        End Using
    End Function

    Private Function GetHardwareID() As String
        Try
            Dim driveLetter As String = Path.GetPathRoot(appPath).TrimEnd("\"c)
            Dim query As String = "Select VolumeSerialNumber From Win32_LogicalDisk Where DeviceID = '" & driveLetter & "'"
            Dim mbs As New ManagementObjectSearcher(query)
            For Each mo As ManagementObject In mbs.Get()
                Return mo("VolumeSerialNumber").ToString().Trim()
            Next
        Catch
        End Try
        Return "GENERIC-DISK-ID"
    End Function

    ' UI NAVIGATION HANDLERS (FORMAT STANDAR)
    Private Sub BtnHome_Click(sender As Object, e As RoutedEventArgs)
        SetPage(PageHome)
    End Sub
    Private Sub BtnGames_Click(sender As Object, e As RoutedEventArgs)
        SetPage(PageGames)
    End Sub
    Private Sub BtnSettings_Click(sender As Object, e As RoutedEventArgs)
        SetPage(PageSettings)
    End Sub

    Private Sub SetPage(target As UIElement)
        If PageHome IsNot Nothing Then PageHome.Visibility = Visibility.Collapsed
        If PageGames IsNot Nothing Then PageGames.Visibility = Visibility.Collapsed
        If PageSettings IsNot Nothing Then PageSettings.Visibility = Visibility.Collapsed
        If target IsNot Nothing Then target.Visibility = Visibility.Visible
    End Sub

    Private Sub BtnMin_Click(sender As Object, e As RoutedEventArgs)
        Me.WindowState = WindowState.Minimized
    End Sub
    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs)
        ExecuteBypass(False)
        Application.Current.Shutdown()
    End Sub
    Private Sub Window_MouseDown(sender As Object, e As MouseButtonEventArgs)
        If e.ChangedButton = MouseButton.Left Then
            Me.DragMove()
        End If
    End Sub

    ' EULA & ACTIVATION UI HANDLERS
    Private Sub BtnAcceptEula_Click(sender As Object, e As RoutedEventArgs)
        Try
            System.IO.File.WriteAllText(eulaPath, "AGREED|" & DateTime.Now.ToString())
            EnterMainMenu()
        Catch
        End Try
    End Sub
    Private Sub BtnDeclineEula_Click(sender As Object, e As RoutedEventArgs)
        Application.Current.Shutdown()
    End Sub
    Private Sub BtnActivate_Click(sender As Object, e As RoutedEventArgs)
        If TxtHardwareID IsNot Nothing Then TxtHardwareID.Text = GetHardwareID()
        If ActivationOverlay IsNot Nothing Then ActivationOverlay.Visibility = Visibility.Visible
    End Sub
    Private Sub BtnCancelActiv_Click(sender As Object, e As RoutedEventArgs)
        If ActivationOverlay IsNot Nothing Then ActivationOverlay.Visibility = Visibility.Collapsed
    End Sub
    Private Sub BtnSubmitKey_Click(sender As Object, e As RoutedEventArgs)
        Dim inputKey = TxtLicenseInput.Text.Trim().ToUpper()
        If inputKey.Length < 20 Then Return
        Try
            Dim datePart = inputKey.Substring(inputKey.Length - 8)
            Dim signatureInput = inputKey.Substring(0, inputKey.Length - 9)
            If signatureInput = GenerateSecureHash(TxtHardwareID.Text, datePart) Then
                System.IO.File.WriteAllText(datPath, signatureInput & "|" & datePart)
                System.IO.File.SetAttributes(datPath, FileAttributes.Hidden Or FileAttributes.System)
                MessageBox.Show("ACTIVATED!")
                ActivationOverlay.Visibility = Visibility.Collapsed
                CheckLicenseStatus()
            Else
                TxtMsg.Text = "INVALID KEY"
            End If
        Catch
        End Try
    End Sub

    ' SUPPORT & TOOLS HANDLERS
    Private Sub BtnSupport_Click(sender As Object, e As RoutedEventArgs)
        If SupportOverlay IsNot Nothing Then SupportOverlay.Visibility = Visibility.Visible
    End Sub
    Private Sub BtnCloseSupport_Click(sender As Object, e As RoutedEventArgs)
        If SupportOverlay IsNot Nothing Then SupportOverlay.Visibility = Visibility.Collapsed
    End Sub
    Private Sub Hyperlink_RequestNavigate(sender As Object, e As RequestNavigateEventArgs)
        Try
            Process.Start(New ProcessStartInfo(e.Uri.AbsoluteUri) With {.UseShellExecute = True})
        Catch
        End Try
        e.Handled = True
    End Sub

    Private Async Sub BtnClearCache_Click(sender As Object, e As RoutedEventArgs)
        If TxtStatus IsNot Nothing Then TxtStatus.Text = "Closing Roblox..."
        Await Task.Run(Sub() KillRobloxBrutal())
        Dim targets As String() = {"logs", "http", "downloads", "rmcontent", "archived-games"}
        Dim successCount As Integer = 0
        Dim failCount As Integer = 0
        Await Task.Run(Sub()
                           For Each folderName In targets
                               Try
                                   Dim targetPath As String = Path.Combine(serverLocalData, folderName)
                                   If Directory.Exists(targetPath) Then
                                       Directory.Delete(targetPath, True)
                                       successCount += 1
                                   End If
                               Catch
                                   failCount += 1
                               End Try
                           Next
                       End Sub)
        If TxtStatus IsNot Nothing Then TxtStatus.Text = "Cache Cleaned."
        MessageBox.Show($"Selesai! {successCount} folder dibersihkan, {failCount} gagal.", "Cache Cleaner")
    End Sub

    Private Async Sub BtnForceUpdate_Click(sender As Object, e As RoutedEventArgs)
        Dim btn As System.Windows.Controls.Button = TryCast(sender, System.Windows.Controls.Button)
        If btn IsNot Nothing Then btn.IsEnabled = False
        If TxtStatus IsNot Nothing Then TxtStatus.Text = "Stopping Processes..."
        Await Task.Run(Sub() KillRobloxBrutal())
        If TxtStatus IsNot Nothing Then TxtStatus.Text = "Updating Junctions..."
        Dim result As Boolean = Await Task.Run(Function() ForceUpdateJunctions())
        If result Then
            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Ready."
            MessageBox.Show("Junction berhasil diperbarui!", "Success")
        Else
            If TxtStatus IsNot Nothing Then TxtStatus.Text = "Error."
            MessageBox.Show("Gagal. Jalankan sebagai Administrator.", "Failed")
        End If
        If btn IsNot Nothing Then btn.IsEnabled = True
    End Sub
End Class