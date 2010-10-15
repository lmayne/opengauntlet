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