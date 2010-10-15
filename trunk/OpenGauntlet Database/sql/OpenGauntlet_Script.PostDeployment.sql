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
