
#region Imports

using System;
using System.Text;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;
using Proxy = Microsoft.TeamFoundation.Build.Proxy;
using Microsoft.TeamFoundation.Build.Common;
using System.Collections;
using System.Net;
using System.Net.Mail;
using System.Xml;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

#endregion

namespace Org.OpenGauntlet
{
    /// <summary>
    /// Static class to check in shelved TFS changes on
    /// behalf of another user after ensuring the changes
    /// build OK.
    /// </summary>
    class Gauntlet
    {
        #region Class Data

        private static string _strTeamFoundationServerUrl;
        private static string _strWorkspaceLocation;
        private static string _strTeamProject;
        private static string _strShelvesetPrefix;
        private static string _strTeamBuildType;
        private static TeamFoundationServer _objTfsServer;
        private static VersionControlServer _objTfsVersionControlServer;
        private static Workspace _objWorkspace;
        private static string _objBuildErrorMessage;
        private static string _strBuildUserName;
        private static string _strBuildMachine;
        private static string _strBuildDirectory;
        private static string _strTempShelvesetName;
        private static string _strSmtpServer;
        private static int _intSmtpPort;
        private static string _strTfsUsername;
        private static string _strTfsPassword;
        private static string _strDefaultEmailAddress;
        private static string _strRejectedShelvesetName;
        private static string _strConnectionString;
        private static SqlConnection _cnGauntletDatabase;
        private static string _strProfileName;
        private static int _intBuildId;
        private static string _strLogFile;
        private static TextWriter _twLogFile;
        private static string _strAllowedPath;
        private static string _strGlobalWorkspaceFolder;

        private enum BuildStatus
        {
            Started = 0,
            Unshelving = 1,
            Building = 2,
            CheckingIn = 3,
            Passed = 4,
            Failed = 5,
            Stopped = 6
        }

        #endregion

        #region Properties

        /// <summary>
        /// Connection to the OpenGauntlet database to
        /// hold historical information
        /// </summary>
        private static SqlConnection GauntletDatabase
        {
            get
            {
                if (_cnGauntletDatabase == null)
                {
                    _cnGauntletDatabase = new SqlConnection(_strConnectionString);
                }
                if (_cnGauntletDatabase.State == ConnectionState.Closed)
                {
                    _cnGauntletDatabase.Open();
                }
                return _cnGauntletDatabase;
            }
        }

        /// <summary>
        /// The team foundation server to work with
        /// </summary>
        private static TeamFoundationServer TfsServer
        {
            get
            {
                if (_objTfsServer == null)
                {
                    _objTfsServer = new TeamFoundationServer(_strTeamFoundationServerUrl, TfsCredentials);
                }
                return _objTfsServer;
            }
        }

        /// <summary>
        /// Returns the credentials selected by the user
        /// </summary>
        private static ICredentials TfsCredentials
        {
            get
            {
                if (string.IsNullOrEmpty(_strTfsUsername))
                {
                    return CredentialCache.DefaultCredentials;
                }
                else
                {
                    string[] strBits = _strTfsUsername.Split(new char[] { Convert.ToChar("\\") });
                    return new NetworkCredential(strBits[1], _strTfsPassword, strBits[0]);
                }
            }
        }

        /// <summary>
        /// A reference to the TFS server's version control system
        /// </summary>
        private static VersionControlServer TfsVersionControlServer
        {
            get
            {
                if (_objTfsVersionControlServer == null)
                {
                    _objTfsVersionControlServer = (VersionControlServer)TfsServer.GetService(typeof(VersionControlServer));
                }
                return _objTfsVersionControlServer;
            }
        }

        /// <summary>
        /// The temp workspace to use for shelving, unshelving and checking in
        /// </summary>
        private static Workspace TfsWorkspace
        {
            get
            {
                if (_objWorkspace == null)
                {
                    _objWorkspace = TfsVersionControlServer.GetWorkspace(_strWorkspaceLocation);
                }
                return _objWorkspace;
            }
        }

        /// <summary>
        /// Error message to return to the user if the check in failed
        /// </summary>
        private static string BuildErrorMessage
        {
            get
            {
                if (string.IsNullOrEmpty(_objBuildErrorMessage))
                {
                    _objBuildErrorMessage = "";
                }
                return _objBuildErrorMessage;
            }
            set
            {
                _objBuildErrorMessage = value;
            }
        }

        /// <summary>
        /// Name of the temporary shelveset. Always the project name
        /// with a descriptive string suffix
        /// </summary>
        private static string TempShelvesetName
        {
            get
            {
                if (string.IsNullOrEmpty(_strTempShelvesetName))
                {
                    _strTempShelvesetName = _strTeamProject + " " + _strProfileName + " OpenGauntlet Build";
                }
                return _strTempShelvesetName;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Writes a message to the logfile (or the console, if none specified)
        /// </summary>
        /// <param name="pstrMessage">The message to write</param>
        private static void LogMessage(string pstrMessage)
        {
            // Get rid of any newlines for the log file and add the timestamp
            pstrMessage = TimeStamp() + pstrMessage.Replace("\n", "");

            if (_twLogFile == null)
            {
                // See if we need to open a textwriter
                if (string.IsNullOrEmpty(_strLogFile))
                {
                    // No logfile specified, just output to STDOUT
                    Console.WriteLine(pstrMessage);
                }
                else
                {
                    // Need to open the textwriter
                    _twLogFile = new StreamWriter(_strLogFile, true);
                    _twLogFile.WriteLine(pstrMessage);
                    _twLogFile.Flush();
                }
            }
            else
            {
                // Logfile is already open
                _twLogFile.WriteLine(pstrMessage);
                _twLogFile.Flush();
            }
        }

        /// <summary>
        /// Returns the date and time formatted for outputting to the console
        /// </summary>
        /// <returns></returns>
        private static string TimeStamp()
        {
            return DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + ": ";
        }

        /// <summary>
        /// Retrieves all the shelvesets from the server with a matching prefix
        /// </summary>
        /// <returns>Arraylist of shelvesets</returns>
        private static ArrayList GetShelvesets()
        {
            // Get all the shelvesets from the server
            Shelveset[] objAllShelvesets = TfsVersionControlServer.QueryShelvesets(null, null);

            // Create an arraylist which will hold filtered shelvesets
            ArrayList objGauntletShelvesets = new ArrayList();

            // Loop through the returned shelvesets, adding them
            // to our arraylist if their prefix maches the defined prefix
            foreach (Shelveset objShelveset in objAllShelvesets)
            {
                if (objShelveset.Name.Length >= _strShelvesetPrefix.Length && objShelveset.Name.Substring(0, _strShelvesetPrefix.Length).Equals(_strShelvesetPrefix) == true)
                {
                    objGauntletShelvesets.Add(objShelveset);
                }
            }
            // Return the list
            return objGauntletShelvesets;
        }

        /// <summary>
        /// Returns the earliest created shelveset in the passed in collection
        /// </summary>
        /// <param name="plstShelvesetList">List of shelvesets</param>
        /// <returns>Shelveset</returns>
        private static Shelveset GetFirstShelveset(ArrayList plstShelvesetList)
        {
            Shelveset objEarliestSet = null;

            // Just bomb out if there are no shelvesets in the collection
            if (plstShelvesetList.Count == 0)
            {
                return objEarliestSet;
            }

            // Start with the first in the list
            objEarliestSet = (Shelveset)plstShelvesetList[0];

            if (plstShelvesetList.Count > 1)
            {
                // Compare with all the others
                for (int i = 1; i < plstShelvesetList.Count - 1; i++)
                {
                    Shelveset objTempSet = (Shelveset)plstShelvesetList[i];
                    // If this is earlier than the previous one then replace the reference
                    if (objTempSet.CreationDate < objEarliestSet.CreationDate)
                    {
                        objEarliestSet = objTempSet;
                    }
                }
            }

            // Return the shelveset
            return objEarliestSet;
        }

        /// <summary>
        /// Does a quick check to make sure another build isn't running
        /// </summary>
        /// <returns>True if another build is running</returns>
        private static bool CheckBuild()
        {
            SqlCommand cdCheckBuild = new SqlCommand("uspCheckBuild");
            cdCheckBuild.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdCheckBuild.Parameters.Add(new SqlParameter("@pstrProfileName", _strProfileName));
                SqlParameter prmBuildRunning = new SqlParameter("", SqlDbType.Bit);
                prmBuildRunning.Direction = ParameterDirection.ReturnValue;
                cdCheckBuild.Parameters.Add(prmBuildRunning);

                // Execute the command
                cdCheckBuild.Connection = GauntletDatabase;
                cdCheckBuild.ExecuteNonQuery();

                // Check the return value
                if ((int)prmBuildRunning.Value == 0)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdCheckBuild != null)
                {
                    cdCheckBuild.Dispose();
                }
            }
        }

        /// <summary>
        /// Checks to make sure another build isn't already running.
        /// Adds an entry to the database if one is running
        /// </summary>
        private static void CheckAndStartBuild()
        {
            SqlCommand cdStartBuild = new SqlCommand("uspStartBuild");
            cdStartBuild.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdStartBuild.Parameters.Add(new SqlParameter("@pstrProfileName", _strProfileName));
                SqlParameter prmBuildNumber = new SqlParameter("@pintBuildId", SqlDbType.Int);
                prmBuildNumber.Direction = ParameterDirection.Output;
                cdStartBuild.Parameters.Add(prmBuildNumber);

                // Execute the command
                cdStartBuild.Connection = GauntletDatabase;
                cdStartBuild.ExecuteNonQuery();

                // Check the return value
                _intBuildId = (int)prmBuildNumber.Value;
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdStartBuild != null)
                {
                    cdStartBuild.Dispose();
                }
            }
        }

        /// <summary>
        /// Updates the build status in the database
        /// </summary>
        /// <param name="penumStatus"></param>
        private static void UpdateBuildStatus(BuildStatus penumStatus)
        {
            SqlCommand cdUpdateStatus = new SqlCommand("uspUpdateStatus");
            cdUpdateStatus.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdUpdateStatus.Parameters.Add(new SqlParameter("@pintBuildId", _intBuildId));
                cdUpdateStatus.Parameters.Add(new SqlParameter("@pintStatusId", penumStatus));

                // Execute the command
                cdUpdateStatus.Connection = GauntletDatabase;
                cdUpdateStatus.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdUpdateStatus != null)
                {
                    cdUpdateStatus.Dispose();
                }
            }
        }

        /// <summary>
        /// Updates the build number in the database with the completed
        /// TFS build number for this run
        /// </summary>
        /// <param name="pstrBuildNumber">The database BuildId of the build to update</param>
        private static void UpdateBuildNumber(string pstrBuildNumber)
        {
            SqlCommand cdUpdateBuildNumber = new SqlCommand("uspUpdateBuildNumber");
            cdUpdateBuildNumber.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdUpdateBuildNumber.Parameters.Add(new SqlParameter("@pintBuildId", _intBuildId));
                cdUpdateBuildNumber.Parameters.Add(new SqlParameter("@pstrBuildNumber", pstrBuildNumber));

                // Execute the command
                cdUpdateBuildNumber.Connection = GauntletDatabase;
                cdUpdateBuildNumber.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdUpdateBuildNumber != null)
                {
                    cdUpdateBuildNumber.Dispose();
                }
            }
        }

        /// <summary>
        /// Sets the invoked by username and shelveset name for this build
        /// </summary>
        /// <param name="pobjShelveset">The user's shelveset to pick information out of</param>
        private static void UpdateInvokedBy(Shelveset pobjShelveset)
        {
            SqlCommand cdUpdateInvokedBy = new SqlCommand("uspUpdateInvokedBy");
            cdUpdateInvokedBy.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdUpdateInvokedBy.Parameters.Add(new SqlParameter("@pintBuildId", _intBuildId));
                cdUpdateInvokedBy.Parameters.Add(new SqlParameter("@pstrInvokedBy", pobjShelveset.OwnerName));
                cdUpdateInvokedBy.Parameters.Add(new SqlParameter("@pstrShelvesetName", pobjShelveset.Name));

                // Execute the command
                cdUpdateInvokedBy.Connection = GauntletDatabase;
                cdUpdateInvokedBy.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdUpdateInvokedBy != null)
                {
                    cdUpdateInvokedBy.Dispose();
                }
            }
        }

        /// <summary>
        /// Updates the build end date in the database
        /// </summary>
        private static void SetBuildEndDate()
        {
            SqlCommand cdSetEndDate = new SqlCommand("uspSetBuildEnd");
            cdSetEndDate.Parameters.Add(new SqlParameter("@pintBuildId", _intBuildId));
            cdSetEndDate.CommandType = CommandType.StoredProcedure;
            try
            {
                // Execute the command
                cdSetEndDate.Connection = GauntletDatabase;
                cdSetEndDate.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdSetEndDate != null)
                {
                    cdSetEndDate.Dispose();
                }
            }
        }

        /// <summary>
        /// Deletes the entire contents of a directory
        /// after removing readonly property from all files
        /// </summary>
        /// <param name="strPath"></param>
        private static void DeleteDirectory(string strPath)
        {
            string[] strFiles = Directory.GetFiles(strPath, "*", SearchOption.AllDirectories);
            foreach (string strFile in strFiles)
            {
                FileAttributes faAttributes = File.GetAttributes(strFile);
                if ((faAttributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(strFile, ~FileAttributes.ReadOnly);
                    File.Delete(strFile);
                }
            }
            Directory.Delete(strPath, true);
        }

        /// <summary>
        /// Performs a get latest on the workspace, overwriting any changes
        /// </summary>
        private static void UpdateWorkspace()
        {
            // Undo any changes
            TfsWorkspace.Undo("$/", RecursionType.Full);

            // Delete the contents of the workspace. This is important, as
            // someone's Add change may have failed, and a writable copy of
            // the file may be left behind
            DeleteDirectory(_strWorkspaceLocation);

            // Get latest
            TfsWorkspace.Get(VersionSpec.Latest, GetOptions.GetAll | GetOptions.Overwrite);
        }

        /// <summary>
        /// Kicks off the build and waits for it to finish
        /// </summary>
        /// <returns>Boolean true if everything is OK. If the build fails,
        /// returns false and sets the global build string</returns>
        private static bool StartBuild()
        {
            // Update the database status
            UpdateBuildStatus(BuildStatus.Building);

            // Create required objects
            Proxy.BuildController buildCtrl = (Proxy.BuildController)TfsServer.GetService(typeof(Proxy.BuildController));
            Proxy.BuildStore store = (Proxy.BuildStore)TfsServer.GetService(typeof(Proxy.BuildStore));
            Proxy.BuildParameters buildParam = new Proxy.BuildParameters();

            buildParam.TeamFoundationServer = _strTeamFoundationServerUrl;
            buildParam.BuildType = _strTeamBuildType;
            buildParam.TeamProject = _strTeamProject;
            buildParam.BuildDirectory = _strBuildDirectory;
            buildParam.BuildMachine = _strBuildMachine;
            buildCtrl.Credentials = TfsCredentials;
            string buildUri = buildCtrl.StartBuild(buildParam);

            // wait until the build completes
            BuildConstants.BuildStatusIconID status;
            bool buildComplete = false;
            string strBuildNumber = "";

            do
            {
                Proxy.BuildData bd = store.GetBuildDetails(buildUri);
                status = (BuildConstants.BuildStatusIconID)bd.BuildStatusId;
                if (string.IsNullOrEmpty(strBuildNumber))
                {
                    strBuildNumber = bd.BuildNumber;
                    // Update the build number in the database
                    UpdateBuildNumber(strBuildNumber);
                }
                buildComplete = (status == BuildConstants.BuildStatusIconID.BuildSucceeded ||
                    status == BuildConstants.BuildStatusIconID.BuildFailed ||
                    status == BuildConstants.BuildStatusIconID.BuildStopped);
            } while (!buildComplete);

            if (status == BuildConstants.BuildStatusIconID.BuildFailed || status == BuildConstants.BuildStatusIconID.BuildStopped)
            {
                // Build failed. Find out why

                // Check unit tests
                foreach (Proxy.PlatformFlavorData objFlavor in store.GetPlatformFlavorsForBuild(buildUri))
                {
                    Proxy.TestResultData[] testResults = store.GetTestResultsForBuild(buildUri, objFlavor.PlatformName, objFlavor.FlavorName);
                    foreach (Proxy.TestResultData testResult in testResults)
                    {
                        if (!testResult.RunPassed)
                        {
                            BuildErrorMessage = string.Format("Unit tests for build failed. See build number '{0}' " +
                                "for more details", strBuildNumber);
                            return false;
                        }
                    }

                }

                // If the unit tests were OK, the compilation must have failed
                if (string.IsNullOrEmpty(BuildErrorMessage))
                {
                    BuildErrorMessage = string.Format("Code compilation failed. See build number '{0}' " +
                        "for more details", strBuildNumber);
                    return false;
                }
            }

            // No problems
            return true;
        }

        /// <summary>
        /// Performs a check in of the changes in the workspace
        /// using the settings in the passed in shelveset
        /// </summary>
        /// <param name="pobjShelveset">The shelveset to use for check in settings</param>
        /// <returns>Changeset ID</returns>
        private static int CheckInChanges(Shelveset pobjShelveset)
        {
            // Update the database status
            UpdateBuildStatus(BuildStatus.CheckingIn);

            // IF they have overriden any checkin policies then
            // we need to specify why
            PolicyOverrideInfo objOverride = null;
            if (string.IsNullOrEmpty(pobjShelveset.PolicyOverrideComment) == false)
            {
                objOverride = new PolicyOverrideInfo(pobjShelveset.PolicyOverrideComment,
                    TfsWorkspace.EvaluateCheckin(CheckinEvaluationOptions.Policies,
                    TfsWorkspace.GetPendingChanges(), TfsWorkspace.GetPendingChanges(),
                    pobjShelveset.Comment, pobjShelveset.CheckinNote, pobjShelveset.WorkItemInfo).PolicyFailures);
            }

            // Check in the changes
            return TfsWorkspace.CheckIn(TfsWorkspace.GetPendingChanges(), pobjShelveset.OwnerName,
                pobjShelveset.Comment, pobjShelveset.CheckinNote, pobjShelveset.WorkItemInfo, objOverride);
        }

        /// <summary>
        /// Deletes the specified shelveset from the server
        /// </summary>
        /// <param name="pobjShelveset">The shelveset to delete</param>
        private static void DeleteShelveset(Shelveset pobjShelveset)
        {
            TfsVersionControlServer.DeleteShelveset(pobjShelveset);
        }

        /// <summary>
        /// Reshelves changes under a new name and deletes the old shelveset
        /// </summary>
        /// <param name="pobjShelveset">The shelveset to delete</param>
        private static void RenameRejectedShelveset(Shelveset pobjShelveset, Workspace pobjWorkspace)
        {
            if (pobjWorkspace.GetPendingChanges().Length > 0)
            {
                _strRejectedShelvesetName = "REJECTED-" + pobjShelveset.Name;

                // If maximum shelveset name length is 64 chars
                // Trim to 62 to take account of adding increment numbers
                if (_strRejectedShelvesetName.Length > 62)
                {
                    _strRejectedShelvesetName = _strRejectedShelvesetName.Substring(0, 62);
                }

                // TFS does not allow shelving with a space at the end of the name
                _strRejectedShelvesetName = _strRejectedShelvesetName.TrimEnd(new char[] { Convert.ToChar(" ") });


                int i = 0;
                bool blnNameOK = false;
                do
                {
                    // Make sure the new shelveset name doesn't exist
                    if (TfsVersionControlServer.QueryShelvesets(_strRejectedShelvesetName, _strBuildUserName).Length > 0)
                    {
                        // Already exists, add a number to the end
                        if (i == 0)
                        {
                            _strRejectedShelvesetName += ".1";
                            i++;
                        }
                        else
                        {
                            i++;
                            _strRejectedShelvesetName = _strRejectedShelvesetName.Substring(0, _strRejectedShelvesetName.Length - 1) + i.ToString();
                        }
                    }
                    else
                    {
                        blnNameOK = true;
                    }

                } while (blnNameOK == false);

                Shelveset objNewShelveset = new Shelveset(TfsVersionControlServer, _strRejectedShelvesetName, _strBuildUserName);
                objNewShelveset.Comment = pobjShelveset.Comment;
                objNewShelveset.CheckinNote = pobjShelveset.CheckinNote;
                objNewShelveset.WorkItemInfo = pobjShelveset.WorkItemInfo;
                objNewShelveset.PolicyOverrideComment = pobjShelveset.PolicyOverrideComment;
                pobjWorkspace.Shelve(objNewShelveset, pobjWorkspace.GetPendingChanges(), ShelvingOptions.Replace);
                TfsVersionControlServer.DeleteShelveset(pobjShelveset);
            }
        }

        /// <summary>
        /// Unshelves the shelveset into the active workspace
        /// </summary>
        /// <param name="pobjShelveset">The working shelveset</param>
        /// <returns>True for success</returns>
        private static bool UnshelveIntoTempWorkspace(Shelveset pobjShelveset)
        {
            // First make sure there are no pending changes (shouldn't be, but undo anyway)
            TfsWorkspace.Undo("$/", RecursionType.Full);

            // Make sure the changes are within the allowed path before unshelving
            PendingSet[] objPendingSets = TfsVersionControlServer.QueryShelvedChanges(pobjShelveset.Name, pobjShelveset.OwnerName, null, true);
            PendingChange[] objShelvedChanges = objPendingSets[0].PendingChanges;
            // If they have not set an allowed path then use the TFS project path
            string strStem = "";
            if (string.IsNullOrEmpty(_strAllowedPath))
            {
                strStem = "$/" + _strTeamProject;
            }
            else
            {
                strStem = _strAllowedPath;
            }
            foreach (PendingChange objChange in objShelvedChanges)
            {
                if (string.Compare(objChange.ServerItem.Substring(0, strStem.Length), strStem, true) != 0)
                {
                    BuildErrorMessage = "There are pending changes in the shelveset which " +
                        "are not against the specified project or branch. Have you used the correct name prefix?" +
                        Environment.NewLine + "Change is against " + objChange.ServerItem;

                    // We now have a problem; we need to reshelve the changes without unshelving into our
                    // temp workspace which may not contain mappings for the file. We'll need to create a 
                    // new workspace and then delete it afterwards
                    string strTempWorkspaceName = Guid.NewGuid().ToString();
                    Workspace objEmergencyWorkspace = TfsVersionControlServer.CreateWorkspace(strTempWorkspaceName, TfsVersionControlServer.AuthenticatedUser);
                    objEmergencyWorkspace.Map("$/", Path.Combine(_strGlobalWorkspaceFolder, strTempWorkspaceName));
                    objEmergencyWorkspace.Unshelve(pobjShelveset.Name, pobjShelveset.OwnerName);
                    RenameRejectedShelveset(pobjShelveset, objEmergencyWorkspace);
                    objEmergencyWorkspace.Delete();
                    Directory.Delete(Path.Combine(_strGlobalWorkspaceFolder, strTempWorkspaceName), true);

                    return false;
                }
            }

            // Unshelve the changes
            TfsWorkspace.Unshelve(pobjShelveset.Name, pobjShelveset.OwnerName);

            // Return OK
            return true;
        }

        /// <summary>
        /// Creates a copy of the selected shelveset by unshelving
        /// the changes and evaluating the changes, and then reshelving
        /// </summary>
        /// <param name="pobjShelveset">The working shelveset</param>
        /// <returns>Boolean true for success</returns>
        /// <remarks>
        /// This function will also attempt to merge any conflicts. If
        /// any conflicts are unresolvable, the function will return false
        /// </remarks>
        private static bool CopyActiveShelveset(Shelveset pobjShelveset)
        {
            // Delete the shelveset if it has been left behind
            DeleteActiveShelveset();

            // Unshelve the shelveset into the temp workspace
            if (UnshelveIntoTempWorkspace(pobjShelveset) == false)
            {
                // There was a problem with the unshelve
                throw new Exception(BuildErrorMessage);
            }

            // Check status
            CheckinEvaluationResult objResult = TfsWorkspace.EvaluateCheckin(
                CheckinEvaluationOptions.All, TfsWorkspace.GetPendingChanges(),
                TfsWorkspace.GetPendingChanges(), pobjShelveset.Comment,
                pobjShelveset.CheckinNote, pobjShelveset.WorkItemInfo);

            if (objResult.Conflicts.Length > 0)
            {
                // We need to resolve any conflicts
                // Get latest on each of the files to raise the conflict
                PendingSet[] objPendingSets = TfsVersionControlServer.QueryShelvedChanges(pobjShelveset.Name, pobjShelveset.OwnerName, null, true);
                PendingChange[] objShelvedChanges = objPendingSets[0].PendingChanges;

                foreach (PendingChange objChange in objShelvedChanges)
                {
                    TfsWorkspace.Get(new GetRequest(objChange.ServerItem, RecursionType.None, VersionSpec.Latest), GetOptions.None);
                }

                // Now query the conflicts
                Conflict[] objConflicts = TfsWorkspace.QueryConflicts(new string[] { "$/" + _strTeamProject }, true);
                if (objConflicts.Length > 0)
                {
                    // Resolve conflicts
                    foreach (Conflict objConflict in objConflicts)
                    {
                        if (TfsWorkspace.MergeContent(objConflict, false))
                        {
                            // Make sure the conflict was resolvable
                            if (objConflict.ContentMergeSummary.TotalConflicting == 0)
                            {
                                objConflict.Resolution = Resolution.AcceptMerge;
                                TfsWorkspace.ResolveConflict(objConflict);
                            }
                        }
                        if (objConflict.IsResolved == false)
                        {
                            BuildErrorMessage = "There are conflicts in your shelveset that " +
                                "cannot be merged automatically. Please unshelve your changes, " +
                                "resolve any conflicts manually, and reshelve to check in your changes";
                            return false;
                        }
                    }
                }
            }
            if (EvaluateRequiredCheckinNotes(pobjShelveset.CheckinNote).Length > 0)
            {
                // Checkin notes missing
                BuildErrorMessage += "Required checkin notes are missing" + Environment.NewLine;
            }
            if (objResult.PolicyFailures.Length > 0)
            {
                // Checkin policy failure(s)
                // OK if they have overriden and given a reason
                if (string.IsNullOrEmpty(pobjShelveset.PolicyOverrideComment))
                {
                    BuildErrorMessage += "Check in policy failure (";
                    StringBuilder strMessage = new StringBuilder();
                    foreach (PolicyFailure objFail in objResult.PolicyFailures)
                    {
                        strMessage.Append(objFail.Message + ", ");
                    }
                    BuildErrorMessage += strMessage.ToString().Substring(0, strMessage.Length - 2) + ")";
                    return false;
                }
            }

            // If the unshelve was OK, reshelve and return true
            if (string.IsNullOrEmpty(BuildErrorMessage))
            {
                Shelveset objTempShelveset = new Shelveset(TfsVersionControlServer, TempShelvesetName, _strBuildUserName);
                objTempShelveset.Comment = "OpenGauntlet working shelveset for team project '" +
                    _strTeamProject + "' profile '" + _strProfileName + "'. DO NOT DELETE THIS SHELVESET!!";
                TfsWorkspace.Shelve(objTempShelveset, TfsWorkspace.GetPendingChanges(), ShelvingOptions.None);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Removes the temporary shelveset
        /// </summary>
        private static void DeleteActiveShelveset()
        {
            Shelveset[] objShelvesets = TfsVersionControlServer.QueryShelvesets(TempShelvesetName, _strBuildUserName);
            if (objShelvesets.Length > 0)
            {
                foreach (Shelveset objShelveset in objShelvesets)
                {
                    TfsVersionControlServer.DeleteShelveset(objShelveset);
                }
            }
        }

        /// <summary>
        /// Checks to ensure required checkin note fields have been provided
        /// </summary>
        /// <param name="pobjNotes">The CheckinNote object collection to evaluate</param>
        /// <returns>An array of CheckinNoteFailures, empty if no errors are found</returns>
        private static CheckinNoteFailure[] EvaluateRequiredCheckinNotes(CheckinNote pobjNotes)
        {
            // Get the field definitions
            CheckinNoteFieldDefinition[] objNoteFieldDefs = TfsVersionControlServer.GetCheckinNoteDefinitions(new TeamProject[] { TfsVersionControlServer.GetTeamProject(_strTeamProject) });

            // Create a temp ArrayList we can add any failures to
            ArrayList arrErrors = new ArrayList();

            // Loop through the field definitions
            foreach (CheckinNoteFieldDefinition objNoteFieldDef in objNoteFieldDefs)
            {
                // If the note field definition is required...
                if (objNoteFieldDef.Required == true)
                {
                    // Loop through each passed in checkin note
                    // until we find the correct one
                    for (int i = 0; i < pobjNotes.Values.Length; i++)
                    {
                        // Is this the correct note?
                        if (pobjNotes.Values[i].Name.Equals(objNoteFieldDef.Name))
                        {
                            // Yes, ensure a value has been given
                            if (string.IsNullOrEmpty(pobjNotes.Values[i].Value) == true)
                            {
                                // No value given. Add a new failure to our ArrayList
                                arrErrors.Add(new CheckinNoteFailure(objNoteFieldDef, "The checkin note " + pobjNotes.Values[i].Name + " is required but has not been supplied"));
                            }
                        }
                    }
                }
            }

            // Convert the ArrayList into an object array of type CheckinNoteFailure
            CheckinNoteFailure[] objFailures = new CheckinNoteFailure[arrErrors.Count];
            for (int i = 0; i < arrErrors.Count; i++)
            {
                objFailures[i] = (CheckinNoteFailure)arrErrors[i];
            }

            // Return the result
            return objFailures;
        }

        /// <summary>
        /// Loads the settings for the specified profile from
        /// the settings file
        /// </summary>
        /// <param name="pstrProfileName">The name of the profile to load</param>
        private static void LoadSettings(string pstrProfileName)
        {
            // Load the XML document
            XmlDocument xdProfiles = new XmlDocument();
            xdProfiles.Load("Settings.xml");

            // Loading of global settings here
            _strSmtpServer = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/SmtpServer").InnerText;
            _intSmtpPort = int.Parse(xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/SmtpPort").InnerText);
            _strTfsUsername = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/TfsUsername").InnerText;
            _strTfsPassword = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/TfsPassword").InnerText;
            _strDefaultEmailAddress = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/DefaultEmailAddress").InnerText;
            _strConnectionString = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/ConnectionString").InnerText;
            _strGlobalWorkspaceFolder = xdProfiles.SelectSingleNode("/OpenGauntlet/GlobalSettings/WorkspaceFolder").InnerText;

            // Load profile specific settings
            XmlNodeList xnlProfiles = xdProfiles.SelectNodes("/OpenGauntlet/Profiles/Profile");
            bool blnProfileFound = false;
            foreach (XmlNode xnProfile in xnlProfiles)
            {
                if (xnProfile.Attributes["name"].InnerText.Equals(pstrProfileName))
                {
                    blnProfileFound = true;
                    _strTeamFoundationServerUrl = xnProfile.SelectSingleNode("TeamFoundationServer").InnerText;
                    _strWorkspaceLocation = xnProfile.SelectSingleNode("WorkspaceLocation").InnerText;
                    _strTeamProject = xnProfile.SelectSingleNode("TeamProject").InnerText;
                    _strAllowedPath = xnProfile.SelectSingleNode("AllowedPath").InnerText;
                    _strShelvesetPrefix = xnProfile.SelectSingleNode("ShelvesetPrefix").InnerText;
                    _strTeamBuildType = xnProfile.SelectSingleNode("TeamBuildType").InnerText;
                    _strBuildUserName = xnProfile.SelectSingleNode("BuildUser").InnerText;
                    _strBuildMachine = xnProfile.SelectSingleNode("BuildMachine").InnerText;
                    _strBuildDirectory = xnProfile.SelectSingleNode("BuildDirectory").InnerText;
                    _strLogFile = xnProfile.SelectSingleNode("LogFile").InnerText;

                    // Finished loading, exit out
                    return;
                }
            }
            if (blnProfileFound == false)
            {
                throw new ProfileNotFoundException("The specified profile " + pstrProfileName + " is not defined");
            }
        }

        /// <summary>
        /// Retrieves the user's email address from the team foundation server
        /// </summary>
        /// <param name="pstrUsername">The domain and username of the user 
        /// (in the format Domain\Username)</param>
        /// <returns>String email address</returns>
        private static string GetUserEmail(string pstrUsername)
        {
            IGroupSecurityService gss = (IGroupSecurityService)TfsServer.GetService(typeof(IGroupSecurityService));
            Identity objIdentity = gss.ReadIdentity(SearchFactor.AccountName, pstrUsername, QueryMembership.None);
            if (objIdentity != null)
            {
                return objIdentity.MailAddress;
            }
            else
            {
                throw new Exception("Could not get user's email address from TFS");
            }
        }

        /// <summary>
        /// Sends an email to the user telling them their shelveset is being processed
        /// </summary>
        /// <param name="pobjShelveSet">The shelveset being processed</param>
        private static void SendStartEmail(Shelveset pobjShelveset)
        {
            SmtpClient smtpServer = new SmtpClient(_strSmtpServer, _intSmtpPort);
            MailMessage msgEmail = new MailMessage();
            string strMessage = "";

            MailAddress addFrom = new MailAddress("NoReply@TeamFoundationServer.com", "Team Foundation Server");
            string strToAddress = GetUserEmail(pobjShelveset.OwnerName);
            if (string.IsNullOrEmpty(strToAddress))
            {
                strToAddress = _strDefaultEmailAddress;
            }
            MailAddress addTo = new MailAddress(strToAddress, pobjShelveset.OwnerName);

            strMessage = string.Format(
                "Your checkin request '{0}' for TFS project '{1}' is currently being processed.\n" +
                "Please do not delete the shelveset off the server."
                , pobjShelveset.Name, _strTeamProject);

            // Send the email
            msgEmail.To.Add(addTo);
            msgEmail.From = addFrom;
            msgEmail.Subject = string.Format("OpenGauntlet checkin request '{0}' started", pobjShelveset.Name);
            msgEmail.Body = strMessage;
            smtpServer.Send(msgEmail);
        }

        /// <summary>
        /// Emails the user with the result of the checkin request
        /// </summary>
        /// <param name="pobjShelveset">The user's shelveset</param>
        /// <param name="pintChangeset">The changset number to email, if checkin was successful</param>
        private static void SendEndEmail(Shelveset pobjShelveset, int pintChangeset)
        {
            SmtpClient smtpServer = new SmtpClient(_strSmtpServer, _intSmtpPort);
            MailMessage msgEmail = new MailMessage();
            string strMessage = "";

            MailAddress addFrom = new MailAddress("NoReply@TeamFoundationServer.com", "Team Foundation Server");
            string strToAddress = GetUserEmail(pobjShelveset.OwnerName);
            if (string.IsNullOrEmpty(strToAddress))
            {
                strToAddress = _strDefaultEmailAddress;
            }
            MailAddress addTo = new MailAddress(strToAddress, pobjShelveset.OwnerName);

            // Did the checkin go ok?
            if (string.IsNullOrEmpty(BuildErrorMessage))
            {
                // All OK and checked in. Create a success message
                strMessage = string.Format(
                    "Your checkin request '{0}' for TFS project '{1}' was " +
                    "tested and checked in successfully as changeset number {2}"
                    , pobjShelveset.Name, _strTeamProject, pintChangeset);
            }
            else
            {
                // No, we need to send a failure message
                strMessage = string.Format(
                    "Your checkin request '{0}' for TFS project '{1}' was " +
                    "rejected and no changes were checked in. The reason returned " +
                    "was:\n\n{2}\n\nYour shelveset has been moved to {3} " +
                    " under the username {4} to allow you to unshelve, make any " +
                    "necessary changes, and then reshelve to re-request a checkin"
                    , pobjShelveset.Name, _strTeamProject, BuildErrorMessage, _strRejectedShelvesetName, _strBuildUserName);
            }

            // Send the email
            msgEmail.To.Add(addTo);
            msgEmail.From = addFrom;
            msgEmail.Subject = string.Format("OpenGauntlet checkin request '{0}' finished", pobjShelveset.Name);
            msgEmail.Body = strMessage;
            smtpServer.Send(msgEmail);
        }

        /// <summary>
        /// Kills a build. Used when the database still registers a build as running
        /// even though it has been stopped.
        /// </summary>
        /// <param name="pstrProfileName">The profile to kill</param>
        public static void CancelBuild(string pstrProfileName)
        {
            SqlCommand cdUpdateStatus = new SqlCommand("uspCancelBuild");
            cdUpdateStatus.CommandType = CommandType.StoredProcedure;
            try
            {
                // Add the parameters
                cdUpdateStatus.Parameters.Add(new SqlParameter("@pstrProfileName", pstrProfileName));

                // Execute the command
                cdUpdateStatus.Connection = GauntletDatabase;
                cdUpdateStatus.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                if (cdUpdateStatus != null)
                {
                    cdUpdateStatus.Dispose();
                }
            }
        }

        /// <summary>
        /// Sorts the stack
        /// </summary>
        /// <param name="array">The array to sort</param>
        /// <returns>Sorted ArrayList</returns>
        static ArrayList bubble_sort(ArrayList array)
        {
            int right_border = array.Count - 1;

            do
            {
                int last_exchange = 0;
                Shelveset set1;
                Shelveset set2;
                for (int i = 0; i < right_border; i++)
                {
                    set1 = (Shelveset)array[i];
                    set2 = (Shelveset)array[i + 1];
                    if (set1.CreationDate > set2.CreationDate)
                    {
                        Shelveset temp = (Shelveset)array[i];
                        array[i] = (Shelveset)array[i + 1];
                        array[i + 1] = temp;

                        last_exchange = i;
                    }
                }

                right_border = last_exchange;
            }
            while (right_border > 0);

            return array;
        }

        /// <summary>
        /// Prints out product name and version
        /// </summary>
        private static void PrintInfo()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Console.WriteLine("OpenGauntlet version " + asm.GetName().Version.ToString());
            Console.WriteLine("(C) Copyright Leon Mayne 2007");
            Console.WriteLine("See http://www.opengauntlet.org for full information and updates");
            Console.WriteLine();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("Start an OpenGauntlet run:");
            Console.WriteLine("  OpenGauntlet.exe ProfileName");
            Console.WriteLine("Reset a stuck database build:");
            Console.WriteLine("  OpenGauntlet /reset ProfileName");
            Console.WriteLine("Show profile info and perform basic tests:");
            Console.WriteLine("  OpenGauntlet /info ProfileName");
            Console.WriteLine("View the shelvesets in the queue for a profile:");
            Console.WriteLine("  OpenGauntlet /queue ProfileName");
        }

        #endregion

        #region Main

        /// <summary>
        /// Program entry point
        /// </summary>
        /// <param name="args">Application arguments</param>
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine();
                PrintInfo();
                PrintUsage();
                Console.WriteLine();
                return;
            }

            #region Extra Command Line Switches

            // Check to see if they are performing a quick action
            if (args[0].ToLower().Equals("/reset"))
            {
                // They want to kill the build
                Console.WriteLine();
                PrintInfo();
                if (args.Length == 1)
                {
                    Console.WriteLine("Error: No profile specified!");
                    Console.WriteLine();
                    PrintUsage();
                    return;
                }
                // We need to get the settings first
                LoadSettings(args[1]);
                CancelBuild(args[1]);
                Console.WriteLine("Profile '" + args[1] + "' has been reset");
                return;
            }
            else if (args[0].ToLower().Equals("/info"))
            {
                // They want some config information
                Console.WriteLine();
                PrintInfo();
                if (args.Length == 1)
                {
                    Console.WriteLine("Error: No profile specified!");
                    Console.WriteLine();
                    PrintUsage();
                    return;
                }
                _strProfileName = args[1];
                try
                {
                    LoadSettings(_strProfileName);
                    Console.WriteLine("Information for profile " + _strProfileName + ":");
                    Console.WriteLine("Temporary shelveset name: " + TempShelvesetName + ";" + _strBuildUserName);
                    Console.WriteLine();
                    Console.WriteLine("Testing workspace...");
                    Console.WriteLine("Workspace name: " + TfsWorkspace.DisplayName);
                    Console.WriteLine("Connection to TFS and Workspace OK!");
                    if (TfsWorkspace.IsServerPathMapped("$/" + _strTeamProject))
                    {
                        Console.WriteLine("'$/" + _strTeamProject + "' is mapped OK in the workspace");
                    }
                }
                catch (TeamProjectNotFoundException ex)
                {
                    Console.WriteLine("The team project '" + _strTeamProject + "' could not be found!");
                    Console.WriteLine("Returned error: " + ex.Message);
                }
                catch (TeamFoundationServiceException ex)
                {
                    Console.WriteLine("There was an error connecting to one of the TFS services!");
                    Console.WriteLine("Returned error: " + ex.Message);
                }
                catch (WebException ex)
                {
                    Console.WriteLine("There was an error connecting to TFS!");
                    Console.WriteLine("Returned error: " + ex.Message);
                }
                catch (ProfileNotFoundException)
                {
                    Console.WriteLine("The specified profile does not exist!");
                    Console.WriteLine("Please note that profile names are case sensitive. Check the case and try again.");
                }
                catch (ItemNotMappedException ex)
                {
                    Console.WriteLine("The selected workspace folder does not have TFS mappings");
                    Console.WriteLine("Returned error: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("An unexpected error occured!");
                    Console.WriteLine("Returned error: " + ex.Message);
                }
                // Exit out
                return;
            }
            else if (args[0].ToLower().Equals("/queue"))
            {
                // They want to see what shelvesets are sitting in the stack waiting to be processed
                Console.WriteLine();
                PrintInfo();
                if (args.Length == 1)
                {
                    Console.WriteLine("Error: No profile specified!");
                    Console.WriteLine();
                    PrintUsage();
                    return;
                }
                _strProfileName = args[1];

                try
                {
                    LoadSettings(_strProfileName);
                    Console.WriteLine("Shelveset queue for profile " + _strProfileName);
                    Console.WriteLine();
                    ArrayList objShelvesets = bubble_sort(GetShelvesets());
                    if (objShelvesets.Count == 0)
                    {
                        Console.WriteLine("No shelvesets in the queue");
                    }
                    else
                    {
                        foreach (Shelveset objShelveset in objShelvesets)
                        {
                            Console.WriteLine(objShelveset.Name + " (" + objShelveset.OwnerName + ")");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
                return;
            }
            else if (args[0].ToLower().Equals("/createworkspace"))
            {
                // They want to create the workspace specified in the settings file
                Console.WriteLine();
                PrintInfo();
                if (args.Length == 1)
                {
                    Console.WriteLine("Error: No profile specified!");
                    Console.WriteLine();
                    PrintUsage();
                    return;
                }
                _strProfileName = args[1];

                try
                {
                    LoadSettings(_strProfileName);

                    // Make sure it doesn't already exist
                    try
                    {
                        TfsWorkspace.IsServerPathMapped("$/" + _strTeamProject);
                        Console.WriteLine("The specified workspace appears to already be mapped!");
                        return;
                    }
                    catch (ItemNotMappedException)
                    {
                        // Ask them for the workspace name
                        Console.WriteLine("Please enter a friendly name for the workspace:");
                        string strWorkspaceName = Console.ReadLine();
                        if (string.IsNullOrEmpty(strWorkspaceName))
                        {
                            Console.WriteLine("Error! You must specify a workspace name.");
                            return;
                        }
                        Workspace objNewWorkspace = TfsVersionControlServer.CreateWorkspace(strWorkspaceName, TfsVersionControlServer.AuthenticatedUser);
                        objNewWorkspace.Map("$/" + _strTeamProject, _strWorkspaceLocation);
                        Console.WriteLine("Sucessfully created workspace " + strWorkspaceName);
                        Console.WriteLine("Mapped '$/" + _strTeamProject + "' to '" + _strWorkspaceLocation + "'");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error testing for workspace existence: " + ex.Message);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
                return;
            }

            #endregion

            // This is a proper profile build
            Shelveset objWorkingShelveset = null;
            int intChangeset = 0;

            try
            {
                // Load settings for the supplied profile
                _strProfileName = args[0];
                LoadSettings(_strProfileName);

                // Make sure we are not already running a build
                if (CheckBuild() == true)
                {
                    // Build already running, exit out
                    return;
                }

                // Get all the matching shelvesets from TFS
                ArrayList objShelvesets = GetShelvesets();

                if (objShelvesets.Count == 0)
                {
                    // No matching shelvesets found. Exit out.
                    return;
                }

                // Start the build in the database
                CheckAndStartBuild();
                if (_intBuildId == 0)
                {
                    // Build already running, exit out
                    LogMessage("Another build has started after we began, exiting");
                    LogMessage("This could indicate that your scheduled task runs too often");
                    return;
                }

                LogMessage("Finding first shelveset...");
                objWorkingShelveset = GetFirstShelveset(objShelvesets);

                // Update the build details
                LogMessage("Working with shelveset " + objWorkingShelveset.Name);
                UpdateInvokedBy(objWorkingShelveset);

                // Email the user
                SendStartEmail(objWorkingShelveset);

                // Update the workspace
                LogMessage("Updating local workspace...");
                UpdateWorkspace();

                // Unshelve and copy the shelveset
                LogMessage("Copying shelveset...");
                UpdateBuildStatus(BuildStatus.Unshelving);
                if (CopyActiveShelveset(objWorkingShelveset) == false)
                {
                    // There was a problem with the changes or reshelving
                    throw new Exception(BuildErrorMessage);
                }

                // Undo changes in the temp workspace just in case we have
                // exclusively checked out a binary file
                TfsWorkspace.Undo("$/", RecursionType.Full);

                LogMessage("Starting build...");
                if (StartBuild() == true)
                {
                    // Build suceeded
                    LogMessage("Checking in changes...");
                    if (UnshelveIntoTempWorkspace(objWorkingShelveset) == false)
                    {
                        // There was a problem with the unshelve
                        throw new Exception(BuildErrorMessage);
                    }
                    intChangeset = CheckInChanges(objWorkingShelveset);

                    if (intChangeset == 0)
                    {
                        // Changes were not checked in!
                        throw new Exception("There was a problem checking in the changes!");
                    }

                    LogMessage("Deleting shelveset...");
                    DeleteShelveset(objWorkingShelveset);
                }
                else
                {
                    // Build failed
                    throw new Exception(BuildErrorMessage);
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(BuildErrorMessage))
                {
                    // Set the error to the thrown exception
                    BuildErrorMessage = ex.Message;
                }
                LogMessage("An error occured: " + ex.Message);
                LogMessage("Renaming shelveset...");
                UnshelveIntoTempWorkspace(objWorkingShelveset);
                RenameRejectedShelveset(objWorkingShelveset, TfsWorkspace);
            }
            finally
            {
                if (_intBuildId > 0)
                {
                    // Update the database status
                    if (string.IsNullOrEmpty(BuildErrorMessage) == true)
                    {
                        UpdateBuildStatus(BuildStatus.Passed);
                    }
                    else
                    {
                        UpdateBuildStatus(BuildStatus.Failed);
                    }

                    LogMessage("Undoing workspace changes...");
                    TfsWorkspace.Undo("$/", RecursionType.Full);
                    LogMessage("Deleting temp shelveset...");
                    DeleteActiveShelveset();
                    if (objWorkingShelveset != null)
                    {
                        LogMessage("Emailing user...");
                        try
                        {
                            SendEndEmail(objWorkingShelveset, intChangeset);
                        }
                        catch (Exception ex)
                        {
                            LogMessage("Error sending email: " + ex.Message);
                        }
                    }

                    // Update the database to say that this build has finished
                    SetBuildEndDate();
                    LogMessage("");
                }

                // Make sure the logfile is closed
                if (_twLogFile != null)
                {
                    _twLogFile.Close();
                }
            }
        }

        #endregion

        #region Helper Classes

        public class ProfileNotFoundException : Exception
        {
            public ProfileNotFoundException() : base() { }
            public ProfileNotFoundException(string message) : base(message) { }
        }

        #endregion
    }
}
