-- Fix column sizes in existing SunHotelDB database
USE SunHotelDB;
GO

-- Drop existing constraints first
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'ALTER TABLE [dbo].[booking] DROP CONSTRAINT ' + QUOTENAME(name) + ';'
FROM sys.default_constraints 
WHERE parent_object_id = OBJECT_ID('booking') 
AND col_name(parent_object_id, parent_column_id) = 'booking_code';

EXEC sp_executesql @sql;

ALTER TABLE [dbo].[booking] ALTER COLUMN booking_code NVARCHAR(20) NOT NULL;

SELECT @sql = N'';
SELECT @sql += N'ALTER TABLE [dbo].[ticket] DROP CONSTRAINT ' + QUOTENAME(name) + ';'
FROM sys.default_constraints 
WHERE parent_object_id = OBJECT_ID('ticket') 
AND col_name(parent_object_id, parent_column_id) = 'ticket_number';

EXEC sp_executesql @sql;

ALTER TABLE [dbo].[ticket] ALTER COLUMN ticket_number NVARCHAR(30) NOT NULL;

PRINT 'Database schema updated successfully';
GO
