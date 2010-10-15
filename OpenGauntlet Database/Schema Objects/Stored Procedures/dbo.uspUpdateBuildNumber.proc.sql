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