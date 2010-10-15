/*
Deployment script for OpenGauntlet
*/

GO
SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON;

SET NUMERIC_ROUNDABORT OFF;


GO
:setvar DatabaseName "OpenGauntlet"
:setvar DefaultDataPath "C:\Program Files\Microsoft SQL Server\MSSQL.1\MSSQL\DATA\"
:setvar DefaultLogPath "C:\Program Files\Microsoft SQL Server\MSSQL.1\MSSQL\DATA\"

GO
USE [master]

GO
:on error exit
GO
IF (DB_ID(N'$(DatabaseName)') IS NOT NULL
    AND DATABASEPROPERTYEX(N'$(DatabaseName)','Status') <> N'ONLINE')
BEGIN
    RAISERROR(N'The state of the target database, %s, is not set to ONLINE. To deploy to this database, its state must be set to ONLINE.', 16, 127,N'$(DatabaseName)') WITH NOWAIT
    RETURN
END

GO
IF (DB_ID(N'$(DatabaseName)') IS NOT NULL) 
BEGIN
    ALTER DATABASE [$(DatabaseName)]
    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(DatabaseName)];
END

GO
PRINT N'Creating $(DatabaseName)...'
GO
CREATE DATABASE [$(DatabaseName)]
    ON 
    PRIMARY(NAME = [OpenGauntlet], FILENAME = N'$(DefaultDataPath)OpenGauntlet.mdf')
    LOG ON (NAME = [OpenGauntlet_log], FILENAME = N'$(DefaultLogPath)OpenGauntlet_log.ldf') COLLATE SQL_Latin1_General_CP1_CS_AS
GO
EXECUTE sp_dbcmptlevel [$(DatabaseName)], 90;


GO
IF EXISTS (SELECT 1
           FROM   [master].[dbo].[sysdatabases]
           WHERE  [name] = N'$(DatabaseName)')
    BEGIN
        ALTER DATABASE [$(DatabaseName)]
            SET ANSI_NULLS ON,
                ANSI_PADDING ON,
                ANSI_WARNINGS ON,
                ARITHABORT ON,
                CONCAT_NULL_YIELDS_NULL ON,
                NUMERIC_ROUNDABORT OFF,
                QUOTED_IDENTIFIER ON,
                ANSI_NULL_DEFAULT ON,
                CURSOR_DEFAULT LOCAL,
                RECOVERY FULL,
                CURSOR_CLOSE_ON_COMMIT OFF,
                AUTO_CREATE_STATISTICS ON,
                AUTO_SHRINK OFF,
                AUTO_UPDATE_STATISTICS ON,
                RECURSIVE_TRIGGERS OFF 
            WITH ROLLBACK IMMEDIATE;
        ALTER DATABASE [$(DatabaseName)]
            SET AUTO_CLOSE OFF 
            WITH ROLLBACK IMMEDIATE;
    END


GO
IF EXISTS (SELECT 1
           FROM   [master].[dbo].[sysdatabases]
           WHERE  [name] = N'$(DatabaseName)')
    BEGIN
        ALTER DATABASE [$(DatabaseName)]
            SET ALLOW_SNAPSHOT_ISOLATION OFF;
    END


GO
IF EXISTS (SELECT 1
           FROM   [master].[dbo].[sysdatabases]
           WHERE  [name] = N'$(DatabaseName)')
    BEGIN
        ALTER DATABASE [$(DatabaseName)]
            SET READ_COMMITTED_SNAPSHOT OFF;
    END


GO
IF EXISTS (SELECT 1
           FROM   [master].[dbo].[sysdatabases]
           WHERE  [name] = N'$(DatabaseName)')
    BEGIN
        ALTER DATABASE [$(DatabaseName)]
            SET AUTO_UPDATE_STATISTICS_ASYNC ON,
                PAGE_VERIFY NONE,
                DATE_CORRELATION_OPTIMIZATION OFF,
                DISABLE_BROKER,
                PARAMETERIZATION SIMPLE,
                SUPPLEMENTAL_LOGGING OFF 
            WITH ROLLBACK IMMEDIATE;
    END


GO
IF IS_SRVROLEMEMBER(N'sysadmin') = 1
    BEGIN
        IF EXISTS (SELECT 1
                   FROM   [master].[dbo].[sysdatabases]
                   WHERE  [name] = N'$(DatabaseName)')
            BEGIN
                EXECUTE sp_executesql N'ALTER DATABASE [$(DatabaseName)]
    SET TRUSTWORTHY OFF,
        DB_CHAINING OFF 
    WITH ROLLBACK IMMEDIATE';
            END
    END
ELSE
    BEGIN
        PRINT N'The database settings cannot be modified. You must be a SysAdmin to apply these settings.';
    END


GO
USE [$(DatabaseName)]

GO
IF fulltextserviceproperty(N'IsFulltextInstalled') = 1
    EXECUTE sp_fulltext_database 'disable';


GO
-- Pre-Deployment Script Template							
----------------------------------------------------------------------------------------
-- This file contains SQL statements that will be executed before the build script	
-- Use SQLCMD syntax to include a file into the pre-deployment script			
-- Example:      :r .\filename.sql								
-- Use SQLCMD syntax to reference a variable in the pre-deployment script		
-- Example:      :setvar $TableName MyTable							
--               SELECT * FROM [$(TableName)]					
----------------------------------------------------------------------------------------

GO
PRINT N'Creating [dbo].[tblBuild]...';


GO
CREATE TABLE [dbo].[tblBuild] (
    [BuildId]        INT           IDENTITY (1, 1) NOT NULL,
    [ProfileName]    VARCHAR (255) NOT NULL,
    [InvokedBy]      VARCHAR (100) NULL,
    [ShelvesetName]  VARCHAR (MAX) NULL,
    [TfsBuildNumber] VARCHAR (MAX) NULL,
    [StartDate]      DATETIME      NOT NULL,
    [EndDate]        DATETIME      NULL,
    [StatusId]       TINYINT       NOT NULL,
    PRIMARY KEY CLUSTERED ([BuildId] ASC) WITH (ALLOW_PAGE_LOCKS = ON, ALLOW_ROW_LOCKS = ON, PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF)
);


GO
PRINT N'Creating [dbo].[tblBuild].[IX_tblBuild_EndDate_ProfileName]...';


GO
CREATE NONCLUSTERED INDEX [IX_tblBuild_EndDate_ProfileName]
    ON [dbo].[tblBuild]([EndDate] ASC, [ProfileName] ASC) WITH (ALLOW_PAGE_LOCKS = ON, ALLOW_ROW_LOCKS = ON, PAD_INDEX = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF, ONLINE = OFF, MAXDOP = 0);


GO
PRINT N'Creating [dbo].[tblStatus]...';


GO
CREATE TABLE [dbo].[tblStatus] (
    [StatusId]    TINYINT       NOT NULL,
    [Description] VARCHAR (100) NOT NULL,
    PRIMARY KEY CLUSTERED ([StatusId] ASC) WITH (ALLOW_PAGE_LOCKS = ON, ALLOW_ROW_LOCKS = ON, PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF)
);


GO
PRINT N'Creating FK_tblBuild_StatusId...';


GO
ALTER TABLE [dbo].[tblBuild] WITH NOCHECK
    ADD CONSTRAINT [FK_tblBuild_StatusId] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[tblStatus] ([StatusId]) ON DELETE NO ACTION ON UPDATE NO ACTION;


GO
PRINT N'Creating [dbo].[uspCancelBuild]...';


GO
CREATE PROCEDURE [dbo].[uspCancelBuild]
	@pstrProfileName varchar(255)
	WITH ENCRYPTION
AS
	/*
		Purpose	: Kills a build that has frozen or is not running
		Returns	: N/A
	*/
	
	SET NOCOUNT ON
	
	UPDATE
		tblBuild
	SET
		EndDate = GETDATE(),
		StatusId = 6
	WHERE
		ProfileName = @pstrProfileName
	AND
		EndDate IS NULL

RETURN 0;
GO
PRINT N'Creating [dbo].[uspCheckBuild]...';


GO
CREATE PROCEDURE [dbo].[uspCheckBuild]
	@pstrProfileName VARCHAR(255)
	WITH ENCRYPTION
AS
	/*
		Purpose	: Checks to see if a build is still running
		Returns	: 
	*/
	
	SET NOCOUNT ON
	
	-- Make sure a build is not running
	IF EXISTS
	(
		SELECT
			*
		FROM
			tblBuild WITH (READUNCOMMITTED)
		WHERE
			EndDate IS NULL
		AND
			ProfileName = @pstrProfileName
	)
	BEGIN
		-- A build is running, abort returning true
		RETURN 1
	END
	ELSE
	BEGIN
		-- OK to go!
		RETURN 0
	END
GO
PRINT N'Creating [dbo].[uspSetBuildEnd]...';


GO
CREATE PROCEDURE [dbo].[uspSetBuildEnd]
	@pintBuildId INT
	WITH ENCRYPTION
AS
	/*
		Purpose	: Sets the end datetime of a build
		Returns	: N/A
	*/
	
	SET NOCOUNT ON
	
	UPDATE
		tblBuild
	SET
		EndDate = GETDATE()
	WHERE
		BuildId = @pintBuildId

RETURN 0;
GO
PRINT N'Creating [dbo].[uspStartBuild]...';


GO
CREATE PROCEDURE [dbo].[uspStartBuild]
	@pstrProfileName VARCHAR(255),
	@pintBuildId INT OUTPUT
	WITH ENCRYPTION
AS
	/*
		Purpose	: Checks to see if a build is still running and inserts
				  a new record into tblBuild if not
		Returns	: @pintBuildId: Integer of the build id, zero if not started
	*/
	
	SET NOCOUNT ON
	
	-- Make sure a build is not running
	IF EXISTS
	(
		SELECT
			*
		FROM
			tblBuild WITH (READUNCOMMITTED)
		WHERE
			EndDate IS NULL
		AND
			ProfileName = @pstrProfileName
	)
	BEGIN
		-- A build is running, abort returning zero
		SET @pintBuildId = 0
		RETURN 0
	END
	ELSE
	BEGIN
		-- Insert the new record
		INSERT INTO
			tblBuild
			(
				ProfileName,
				StartDate,
				StatusId
			)
		SELECT
			@pstrProfileName,
			GETDATE(),
			0
		
		-- Get the inserted ID number
		SET @pintBuildId = SCOPE_IDENTITY()
		RETURN 0;
	END
GO
PRINT N'Creating [dbo].[uspUpdateBuildNumber]...';


GO
CREATE PROCEDURE [dbo].[uspUpdateBuildNumber]
	@pintBuildId INT,
	@pstrBuildNumber VARCHAR(MAX)
	WITH ENCRYPTION
AS
	/*
		Purpose	: Sets the TFS build number of a build
		Returns	: N/A
	*/
	
	SET NOCOUNT ON
	
	UPDATE
		tblBuild
	SET
		TfsBuildNumber = @pstrBuildNumber
	WHERE
		BuildId = @pintBuildId

RETURN 0;
GO
PRINT N'Creating [dbo].[uspUpdateInvokedBy]...';


GO
CREATE PROCEDURE [dbo].[uspUpdateInvokedBy]
	@pintBuildId INT,
	@pstrInvokedBy VARCHAR(100),
	@pstrShelvesetName VARCHAR(MAX)
	WITH ENCRYPTION
AS
	/*
		Purpose	: Sets the Invoked By and Shelveset Name fields
		Returns	: N/A
	*/
	
	SET NOCOUNT ON
	
	UPDATE
		tblBuild
	SET
		InvokedBy = @pstrInvokedBy,
		ShelvesetName = @pstrShelvesetName
	WHERE
		BuildId = @pintBuildId
		
RETURN 0;
GO
PRINT N'Creating [dbo].[uspUpdateStatus]...';


GO
CREATE PROCEDURE [dbo].[uspUpdateStatus]
	@pintBuildId INT,
	@pintStatusId TINYINT
	WITH ENCRYPTION
AS
	/*
		Purpose	: Updates the status ID of a build
		Returns	: N/A
	*/
	
	SET NOCOUNT ON
	
	UPDATE
		tblBuild
	SET
		StatusId = @pintStatusId
	WHERE
		BuildId = @pintBuildId

RETURN 0;
GO
-- Refactoring step to update target server with deployed transaction logs
CREATE TABLE  [dbo].[__RefactorLog] (OperationKey UNIQUEIDENTIFIER NOT NULL PRIMARY KEY)
GO
sp_addextendedproperty N'microsoft_database_tools_support', N'refactoring log', N'schema', N'dbo', N'table', N'__RefactorLog'
GO

GO
-- Post-Deployment Script Template							
----------------------------------------------------------------------------------------
-- This file contains SQL statements that will be appended to the build script		
-- Use SQLCMD syntax to include a file into the post-deployment script			
-- Example:      :r .\filename.sql								
-- Use SQLCMD syntax to reference a variable in the post-deployment script		
-- Example:      :setvar $TableName MyTable							
--               SELECT * FROM [$(TableName)]					
----------------------------------------------------------------------------------------

/***************************************
***   Static data management script  ***
***************************************/

-- This script will manage the static data from
-- your Team Database project for tblStatus.

PRINT 'Updating static data table tblStatus'

-- Set to your region's date format to ensure dates are updated correctly
SET DATEFORMAT dmy

-- Turn off affected rows being returned
SET NOCOUNT ON

-- 1: Define table variable
DECLARE @tblTempTable TABLE (
StatusId tinyint,
Description varchar(100)
)

-- 2: Populate the table variable with data
-- This is where you manage your data in source control. You
-- can add and modify entries, but because of potential foreign
-- key contraint violations this script will not delete any
-- removed entries. If you remove an entry then it will no longer
-- be added to new databases based on your schema, but the entry
-- will not be deleted from databases in which the value already exists.
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('0', 'Started')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('1', 'Unshelving')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('2', 'Building')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('3', 'Checking In')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('4', 'Passed')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('5', 'Failed')
INSERT INTO @tblTempTable (StatusId, Description) VALUES ('6', 'Stopped')


-- 3: Insert any new items into the table from the table variable
INSERT INTO tblStatus (StatusId, Description)
SELECT StatusId, Description
FROM @tblTempTable WHERE StatusId NOT IN (SELECT StatusId FROM tblStatus)

-- 4: Update any modified values with the values from the table variable
UPDATE LiveTable SET
LiveTable.Description = tmp.Description
FROM tblStatus LiveTable 
INNER JOIN @tblTempTable tmp ON LiveTable.StatusId = tmp.StatusId

PRINT 'Finished updating static data table tblStatus'

GO
PRINT N'Checking existing data against newly created constraints';


GO
USE [$(DatabaseName)];


GO
ALTER TABLE [dbo].[tblBuild] WITH CHECK CHECK CONSTRAINT [FK_tblBuild_StatusId];


GO
