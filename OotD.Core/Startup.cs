﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Office.Interop.Outlook;
using NLog;
using OotD.Forms;
using OotD.Preferences;
using OotD.Properties;
using Application = Microsoft.Office.Interop.Outlook.Application;
using Exception = System.Exception;

namespace OotD
{
    internal static class Startup
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static Guid LastNextButtonClicked;
        public static Guid LastPreviousButtonClicked;

        public static Application OutlookApp;
        public static NameSpace OutlookNameSpace;
        public static MAPIFolder OutlookFolder;
        public static Explorer OutlookExplorer;

        public static bool UpdateDetected;

        /// <summary>
        /// The main entry point for the application.
        /// We only want one instance of the application to be running.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(ProcessCommandLineArgs);

            Logger.Debug("Checking to see if there is an instance running.");

            using (new Mutex(true, AppDomain.CurrentDomain.FriendlyName, out var createdNew))
            {
                if (createdNew)
                {
                    try
                    {
                        OutlookApp = new Application();
                        OutlookNameSpace = OutlookApp.GetNamespace("MAPI");

                        // Before we do anything else, wait for the RPC server to be available, as the program will crash if it's not.
                        // This is especially likely when OotD is set to start with windows.
                        if (!IsRPCServerAvailable(OutlookNameSpace)) return;

                        OutlookFolder = OutlookNameSpace.GetDefaultFolder(OlDefaultFolders.olFolderCalendar);

                        // WORKAROUND: Beginning with Outlook 2007 SP2, Microsoft decided to kill all outlook instances 
                        // when opening and closing an item from the view control, even though the view control was still running.
                        // The only way I've found to work around it and keep the view control from crashing after opening an item,
                        // is to get this global instance of the active explorer and keep it going until the user closes the app.
                        OutlookExplorer = OutlookFolder.GetExplorer();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(Resources.ErrorInitializingApp + ' ' + ex, Resources.ErrorCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    System.Windows.Forms.Application.EnableVisualStyles();

                    Logger.Info("Starting the instance manager and loading instances.");
                    var instanceManager = new InstanceManager();

                    try
                    {
                        instanceManager.LoadInstances();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Could not load instances");
                        return;
                    }

                    System.Windows.Forms.Application.Run(instanceManager);
                }
                else
                {
                    // let the user know the program is already running.
                    Logger.Warn("Instance is already running, exiting.");
                    MessageBox.Show(Resources.ProgramIsAlreadyRunning, Resources.ProgramIsAlreadyRunningCaption,
                        MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                }
            }            
        }

        private static void ProcessCommandLineArgs(Options opts)
        {
            if (opts.StartDebugger)
            {
                if (!Debugger.IsAttached) Debugger.Launch();
            }

            if (opts.CreateStartupEntry)
            {               
                TaskScheduling.CreateOotDStartupTask(Logger);
                Environment.Exit(0);
            }

            if (opts.RemoveStartupEntry)
            {
                TaskScheduling.RemoveOotDStartupTask(Logger);
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// This method will test that the RPC server is available by calling GetDefaultFolder on the outlook namespace object.
        /// It will try this for up to 1 minute before giving up and showing the user an error message.
        /// </summary>
        /// <param name="outlookNameSpace"></param>
        /// <returns></returns>
        private static bool IsRPCServerAvailable(NameSpace outlookNameSpace)
        {
            int retryCount = 0;
            while (retryCount < 120)
            {
                try
                {
                    outlookNameSpace.GetDefaultFolder(OlDefaultFolders.olFolderCalendar);
                    return true;
                }
                catch (COMException loE)
                {
                    if ((uint)loE.ErrorCode == 0x80010001)
                    {
                        retryCount++;
                        // RPC_E_CALL_REJECTED - sleep half a second then try again
                        Thread.Sleep(500);
                    }
                }
            }

            MessageBox.Show(Resources.ErrorInitializingApp + ' ' + Resources.Windows_RPC_Server_is_not_available, Resources.ErrorCaption,
                MessageBoxButtons.OK, MessageBoxIcon.Error);

            return false;
        }

        public static void DisposeOutlookObjects()
        {
            OutlookExplorer?.Close();

            OutlookExplorer = null;
            OutlookFolder = null;
            OutlookNameSpace = null;
            OutlookApp = null;
        }
    }
}