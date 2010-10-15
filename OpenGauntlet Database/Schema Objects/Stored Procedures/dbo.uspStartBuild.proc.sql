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


	
	