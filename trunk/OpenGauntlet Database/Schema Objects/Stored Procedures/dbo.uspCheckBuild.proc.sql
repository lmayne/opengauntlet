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


	
	