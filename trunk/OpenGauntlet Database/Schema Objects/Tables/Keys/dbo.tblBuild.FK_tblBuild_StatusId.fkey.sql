ALTER TABLE [dbo].[tblBuild]
	ADD CONSTRAINT [FK_tblBuild_StatusId] 
	FOREIGN KEY (StatusId)
	REFERENCES tblStatus (StatusId)	

