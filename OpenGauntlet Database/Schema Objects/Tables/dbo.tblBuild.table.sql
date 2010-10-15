CREATE TABLE [dbo].[tblBuild]
(
	BuildId int NOT NULL PRIMARY KEY IDENTITY,
	ProfileName varchar(255) NOT NULL,
	InvokedBy varchar(100) NULL,
	ShelvesetName varchar(MAX) NULL,
	TfsBuildNumber VARCHAR(MAX) NULL,
	StartDate DATETIME NOT NULL,
	EndDate DATETIME NULL,
	StatusId TINYINT NOT NULL
);
