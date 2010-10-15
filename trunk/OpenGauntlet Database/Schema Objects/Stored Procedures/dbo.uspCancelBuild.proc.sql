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