﻿using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CliWrap.Internal
{
    internal class CliProcess : IDisposable
    {
        private readonly Process _nativeProcess;
        private readonly Signal _exitSignal = new Signal();
        private readonly StringBuilder _standardOutputBuffer = new StringBuilder();
        private readonly Signal _standardOutputEndSignal = new Signal();
        private readonly StringBuilder _standardErrorBuffer = new StringBuilder();
        private readonly Signal _standardErrorEndSignal = new Signal();

        public DateTimeOffset StartTime { get; private set; }

        public DateTimeOffset ExitTime { get; private set; }

        public int ExitCode => _nativeProcess.ExitCode;

        public string StandardOutput { get; private set; }

        public string StandardError { get; private set; }

        public CliProcess(ProcessStartInfo startInfo,
            Action<string> standardOutputObserver = null, Action<string> standardErrorObserver = null)
        {
            // Create underlying process
            _nativeProcess = new Process {StartInfo = startInfo};

            // Configure start info
            _nativeProcess.StartInfo.CreateNoWindow = true;
            _nativeProcess.StartInfo.RedirectStandardOutput = true;
            _nativeProcess.StartInfo.RedirectStandardError = true;
            _nativeProcess.StartInfo.RedirectStandardInput = true;
            _nativeProcess.StartInfo.UseShellExecute = false;

            // Wire exit event
            _nativeProcess.EnableRaisingEvents = true;
            _nativeProcess.Exited += (sender, args) =>
            {
                // Record exit time
                ExitTime = DateTimeOffset.Now;

                // Release signal
                _exitSignal.Release();
            };

            // Wire stdout
            _nativeProcess.OutputDataReceived += (sender, args) =>
            {
                // Actual data
                if (args.Data != null)
                {
                    // Write to buffer and invoke observer
                    _standardOutputBuffer.AppendLine(args.Data);
                    standardOutputObserver?.Invoke(args.Data);
                }
                // Null means end of stream
                else
                {
                    // Flush buffer
                    StandardOutput = _standardOutputBuffer.ToString();

                    // Release signal
                    _standardOutputEndSignal.Release();
                }
            };

            // Wire stderr
            _nativeProcess.ErrorDataReceived += (sender, args) =>
            {
                // Actual data
                if (args.Data != null)
                {
                    // Write to buffer and invoke observer
                    _standardErrorBuffer.AppendLine(args.Data);
                    standardErrorObserver?.Invoke(args.Data);
                }
                // Null means end of stream
                else
                {
                    // Flush buffer
                    StandardError = _standardErrorBuffer.ToString();

                    // Release signal
                    _standardErrorEndSignal.Release();
                }
            };
        }

        public void Start()
        {
            // Start process
            _nativeProcess.Start();

            // Record start time
            StartTime = DateTimeOffset.Now;

            // Begin reading streams
            _nativeProcess.BeginOutputReadLine();
            _nativeProcess.BeginErrorReadLine();
        }

        public void PipeStandardInput(Stream stream)
        {
            // Copy stream and close stdin
            using (_nativeProcess.StandardInput)
                stream.CopyTo(_nativeProcess.StandardInput.BaseStream);
        }

        public async Task PipeStandardInputAsync(Stream stream)
        {
            // Copy stream and close stdin
            using (_nativeProcess.StandardInput)
                await stream.CopyToAsync(_nativeProcess.StandardInput.BaseStream).ConfigureAwait(false);
        }

        public void WaitForExit()
        {
            // Wait until process exits
            _exitSignal.Wait();

            // Wait until streams finished reading
            _standardOutputEndSignal.Wait();
            _standardErrorEndSignal.Wait();
        }

        public async Task WaitForExitAsync()
        {
            // Wait until process exits
            await _exitSignal.WaitAsync().ConfigureAwait(false);

            // Wait until streams finished reading
            await _standardOutputEndSignal.WaitAsync().ConfigureAwait(false);
            await _standardErrorEndSignal.WaitAsync().ConfigureAwait(false);
        }

        public bool TryKill()
        {
            try
            {
                _nativeProcess.Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _nativeProcess.Dispose();
            _exitSignal.Dispose();
            _standardOutputEndSignal.Dispose();
            _standardErrorEndSignal.Dispose();
        }
    }
}