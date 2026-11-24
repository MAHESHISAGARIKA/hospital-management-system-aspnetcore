# Billing and Payments

## Permissions and prices

Enabled Accountant accounts are authorized for this hospital-wide billing ledger. The project has no facility or caseload assignments. Every billing route and service operation checks this role. There is no clinical or financial override for Administrators or other roles. The UI receives only patient identification and approved billing descriptions and source metadata. Sensitive responses use no-store caching, and POSTs require anti-forgery protection.

Configure Consultation, Laboratory, Pharmacy, Admission or Other services. Source mappings are explicit catalog codes for Laboratory and Pharmacy. Admission services can be configured for a manually selected discharged stay. Prices are immutable, non-overlapping periods with an inclusive start and an exclusive end. Bounded periods allow future prices to be added without changing existing periods. A missing price blocks a charge. Free services require an explicit zero price. No default prices are seeded.

## Invoices and clinical integration

Drafts may replace or remove lines. Each invoice has one patient and one service date. Consultation uses the date of a completed appointment. Laboratory uses the request date of an item on a completed lab order. Pharmacy uses the recorded dispensing date and exact dispensed quantity for an individual dispense line. Source dates use Asia/Colombo. Cross-patient links, ineligible sources and mismatched catalog mappings are rejected. Manual lines are limited to configured Other services. Clinical events never generate invoices automatically.

Quantities are positive and use up to three fractional digits. Prices and amounts use LKR to two decimal places. Multiply quantity by configured unit price, round each line with MidpointRounding.AwayFromZero, then sum the rounded lines. The draft snapshots price, description and source. Issuance rechecks eligibility, takes a unique claim per source and snapshots the patient's invoice label and reference. Issued lines and total are immutable. An invoice may be voided with a reason only after all recorded payments have been reversed. Voiding releases source claims and retains the invoice and number. A replacement draft can reference the original voided invoice.

## Payments and reports

Only cash and manually confirmed bank transfers are recorded. Transfers need a unique transaction reference. Never enter bank account details or credentials. Payments are recorded at the current UTC time. The invoice row is locked before recalculating the balance and validating the payment. Actor-scoped request keys return the original receipt for an identical retry and reject a changed payload. The invoice token prevents stale edits and transactions. Unique numbers derive from full GUIDs.

Balance is total less payments without a reversal. An issued zero-price invoice is treated as paid. Reversals are full, require a reason, retain the original payment and receipt, and restore the balance. A reversal records a correction. Returning actual funds requires manual reconciliation under hospital policy. Receipts show the original amount and balance immediately after payment, with a prominent reversal status when applicable.

The dashboard and reports distinguish issued billing totals, valid collections, and outstanding balances. Reports use local date boundaries and exclude reversed payments from collections. Reversal listings use reversal dates. A payment-category filter selects invoices containing that category. It does not allocate payments across invoice lines. Category totals show issued charges excluding voided invoices. They do not establish recognized revenue. Browser printing and Print to PDF cover invoice, receipt and the displayed report page.

Audit entries contain actors, UTC timestamps, entity IDs, action and safe amounts. Transfer references and free-text reversal and void reasons stay in the protected financial records. Changes and their audit events commit together.

## Verification and migration

AddBillingManagement follows AddPharmacyManagement. Generate or manually apply it only against the intended database. Use the [README](../README.md) commands for a separate disposable PostgreSQL integration database. Native guards and contention tests require that database, and the fixtures are synthetic. Discard it after verification because financial history is retained.

1. From an Accountant account, configure fictional prices with adjacent periods. Try a negative price and an overlapping period.
2. Create and edit a draft for a fictional patient. Test a completed consultation, completed lab item or dispensed item. Try a mismatched patient and a duplicate issued source.
3. Issue the draft, confirm the lines can no longer be edited, and print it. Record partial cash and then a bank transfer. Retry an identical submission and try an overpayment.
4. Print the receipt, reverse the transfer, and verify the restored invoice balance and preserved reversed receipt. Confirm void is blocked until all payments have been reversed. Void and create a linked replacement.
5. Check outstanding balances and compare valid collections with issued totals. Try opening a receipt and posting a payment as a Departmental or Administrator account without the Accountant role.

Deferred policies include due dates and aged receivables, formal revenue recognition, general ledger, payment gateways, cards, insurance, taxes, discounts, credit notes, partial refunds, automatic charging, server-side PDFs.


Stage 11 permits one manually selected Admission charge for a discharged stay belonging to the invoice patient. The service date is the Colombo discharge date, quantity is one, and the existing price snapshot and exclusive issue claim rules apply. Ward tariffs and automatic room-days are deferred. Accountant screens receive only source reference, date and quantity.
