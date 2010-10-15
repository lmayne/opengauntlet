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