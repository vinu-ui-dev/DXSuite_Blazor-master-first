-- Removes the demo-only HotelNo asset tags (metadata-content-targeting spec section 2.4).
-- HotelNo is in neither the metadata registry nor any location's participating keys, and no
-- code reads it, so these rows would only surface as orphaned tags. Dev held eight rows, at
-- locations 1044 and 1046; no customer database ever carried the key.
-- Idempotent: a second run deletes nothing.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM dbo.tblAssetContentMetadata
WHERE MetadataKey = 'HotelNo';

PRINT CONCAT('HotelNo asset tags deleted: ', @@ROWCOUNT);

COMMIT TRANSACTION;
