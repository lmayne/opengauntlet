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