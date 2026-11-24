# Pharmacy Management

## Permissions and boundaries

Pharmacists can work with the hospital's pharmacy inventory and eligible finalized prescriptions. This is a single-pharmacy queue. Administrators can manage catalog data and thresholds. A Doctor can see dispensing status for prescriptions within their verified EMR care relationship. Other roles have no stock or pharmacy clinical permissions. Pharmacy cannot edit the signed prescription, and catalog permission grants no clinical access. Sensitive responses use no-store headers and writes require anti-forgery validation.

## Inventory

Catalog codes and medicine-specific batch numbers are normalized and unique. Active medicines accept receipts with a positive quantity, an actual receipt date and unexpired expiry date. Count units are whole numbers. Gram and millilitre permit up to three decimal places. No conversions are inferred. Stock identity and unit are fixed after the first batch. All physical changes append a movement with actor, quantity, unit and timestamp. Adjustments require a reason. Quarantine and recall preserve the physical balance while excluding it from dispensing. An authorized removal records a decrease for expired or held stock. No automatic disposal or return-to-stock is available.

The expiry date remains eligible through the end of that date in Asia/Colombo. The configurable warning windows default to 30, 60 and 90 days. Low-stock uses eligible stock only. It excludes expired, quarantined and recalled stock and highlights zero stock even with a zero threshold. The dashboard and operational pages provide catalog/inventory, movement, consumption, expiry, low-stock and dispensing reports. The browser supports print and Print to PDF for receipts and the currently displayed report page.

## Dispensing and history

1. Open a finalized, authoritative EMR prescription in the pharmacy queue. Verify the patient number, medicine identity and strength, explicit prescribed quantity unit and batch. Unclear or missing information needs a prescriber review.
2. Select eligible stock. The application checks the exact generic or brand name and strength, and exact stock quantity unit. The pharmacist explicitly selects the product and batch. The earliest-expiring eligible batch for that selected product comes first. A later batch requires an override reason.
3. Submit one or more lines. A partial dispense records only the actual quantity. The remaining balance is calculated from immutable dispense lines. The same actor and request key returns the prior receipt for the same normalized submission. A different payload under the same key is rejected.
4. A void or signed successor stops new dispensing from the old revision. After an earlier dispensing, a later revision is held for reconciliation. The system preserves the signed original, its items, and any earlier dispensing receipts. Stage 9 has no automated reversal or return-to-stock function.

The transaction locks the encounter shared with EMR and medicine rows in stable GUID order before rechecking available stock and prescription limits. This also protects FEFO when receiving or adjusting stock concurrently. The balance, receipt, movements and safe audit metadata commit together. Rejected requests write no stock changes. Database constraints protect quantities, relationships and ledger history. UTC timestamps are displayed in Asia/Colombo. Audit entries omit patient names, directions and free-text reasons.

## Migration and verification

The additive AddPharmacyManagement migration depends on AddLaboratoryManagement. Use only an explicitly chosen database for manual migration and a disposable one for integration tests. See [README](../README.md) for commands. The PostgreSQL test uses synthetic fixtures in the disposable test database. Discard that database after testing because immutable history is deliberately retained.

1. Register a fictional medicine, set a threshold and receive two batches with different expiry dates. Try a duplicate batch and a fractional count.
2. As a Doctor, sign a fictional prescription with total quantity and an explicit unit. As Pharmacist, verify the patient number and record a partial dispense. Verify FEFO, then open and print the receipt.
3. Attempt an over-dispense, an expired batch and a quarantined batch. Verify the movement ledger and eligible balance. Recheck the prescription status as the authorized Doctor.
4. Try the prescription URL as an Administrator or unrelated role. Try a stale adjustment and a concurrent last-stock dispense on the disposable database.
5. Void or amend the signed prescription. Check that the old receipt remains available and the affected revision is held.

Controlled-drug and refill rules, controlled returns and reconciliation, multi-location assignments, and server-side PDF export need separate policy or development. Billing may later consume a dispensing reference and approved charge metadata. This stage creates no invoices or payments.
