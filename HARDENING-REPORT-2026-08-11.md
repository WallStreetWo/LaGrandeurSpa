# La Grandeur Spa — Full Hardening Pass

Date: 11 August 2026

This project was reviewed end-to-end from the supplied source ZIP. The goal of this pass was to preserve the existing customer/admin design while correcting data-integrity, booking, invoice, reminder, validation, security and deployment weaknesses.

## Highest-impact fixes

### Database / data integrity

- Replaced the old `EnsureCreatedAsync()`-only startup behaviour with `DatabaseSchemaManager`.
- Existing SQL Server databases are upgraded in place; customer/booking data is not deliberately dropped or recreated.
- Added versioned `AppSchemaMigrations` tracking.
- Added/backfilled invoice snapshot fields, including herbal-tea snapshot data.
- Added SQL Server `rowversion` optimistic concurrency for booking status changes.
- Added startup verification/repair for unique booking-reference, invoice-number and booking-slot indexes.
- Stale slot records belonging to cancelled/declined bookings are removed during the integrity upgrade.
- Duplicate slot/reference/invoice data causes startup to stop with an explicit error rather than silently discarding conflicting booking data.
- Booking-to-service deletion is restrictive so a service cannot cascade-delete booking history.

### Booking collision protection

- Booking creation still uses the unique 15-minute `BookingSlot` database index.
- Added direct appointment-range overlap checks.
- Added server-side booking-grid validation; a tampered time that was never offered by the browser is rejected.
- Availability is rechecked inside a serializable SQL transaction.
- Availability calculation now loads the day's blocked/occupied ranges once instead of repeatedly querying SQL for each candidate slot.
- Rapid browser availability requests are sequenced so an older response cannot overwrite a newer service/date selection.
- Booking references now use a longer random suffix.

### Booking lifecycle

- Pending bookings can be confirmed or declined.
- Confirmed bookings can be completed or cancelled.
- A booking cannot be completed before its appointment has ended.
- Customer cancellation is restricted to Pending/Confirmed bookings and respects the configured cancellation cutoff.
- Declined/cancelled bookings release their reserved slot rows.
- Concurrent admin/customer actions no longer silently overwrite each other.
- Admin history lists newest historical records first while active bookings remain ordered by soonest appointment.

### Historical price / treatment correctness

- Booking and invoice displays now use the immutable booking snapshot instead of today's service values for:
  - treatment name;
  - duration;
  - price; and
  - herbal-tea offer.
- Editing a service later therefore does not rewrite the commercial details of an old booking/invoice.

### Invoice

- Repaired the invoice page structure and print styling.
- Added the actual La Grandeur logo.
- Added customer/admin back-navigation.
- Added invoice links to customer My Bookings and admin booking cards.
- Print mode hides navigation/toolbars correctly.
- Improved mobile invoice rendering.
- Invoice issue dates are converted using the configured Johannesburg spa timezone.
- Removed the unused QuestPDF package because this project currently uses browser Print / Save PDF rather than server-side PDF generation.

### Availability administration

- Changing/closing trading hours is blocked if it would invalidate an active future booking.
- Adding a blocked period is blocked if it overlaps a Pending/Confirmed booking.
- Overlapping blocked periods are rejected.
- Date/time and reason validation was tightened.
- Service duration controls now follow the configured booking interval rather than hard-coding the browser UI to 15 minutes.

### Notifications / email / reminders

- Day-before and same-day in-app reminders now work even when SMTP is disabled.
- Confirmation, decline and spa/customer cancellation email templates use booking snapshots.
- Added decline/cancellation email templates.
- Notification title/message/URL lengths are bounded before persistence.
- Mark-read operations use direct database updates.
- Notification URLs are kept local to the application flow.
- SMTP configuration is validated before the mail sender reports itself as enabled.
- Reminder threshold parsing now rejects impossible times and falls back safely.

### Authentication / security

- Removed the real database password/connection string from committed `appsettings.json`.
- Added .NET user-secrets support for local development.
- Production cookie security requires HTTPS; auth cookies are HttpOnly and SameSite=Lax.
- Login cookies are revalidated against the database so deactivated users and changed roles/emails cannot retain stale access.
- Login/registration are rate-limited per client IP.
- Login errors do not reveal whether an email address exists.
- Duplicate registration is protected by both a precheck and the unique database index.
- Password hashes are transparently rehashed when ASP.NET requests an upgrade.
- Production refuses to create a first admin using the development placeholder credentials.
- If no active admin exists, the configured seed account can be promoted/reactivated and its configured seed password is applied.
- Error handling is now reachable even for anonymous login/register failures.
- Added response headers for MIME sniffing, framing and referrer privacy.

### Validation / reliability

- Service names, prices, durations and colours are validated server-side.
- Customer/admin note lengths are bounded.
- Cancellation cutoff configuration is bounded to a sane maximum.
- All business-time calculations now use `SpaClock` rather than the web server's local timezone.
- Booking-side notification/email failures no longer make the UI report that a committed booking failed.
- Unique SQL errors are classified rather than treating every database error as a booking collision/duplicate registration.

### Package / deployment cleanup

- EF Core SQL Server: 10.0.10.
- EF Core Design: 10.0.10.
- MailKit: 4.17.0.
- Generated `bin`, `obj` and macOS `.DS_Store` files are excluded from the hardened source package.
- README was rewritten with secure configuration, database-upgrade, build, deployment and pre-launch instructions.

## Static validation performed in this environment

Passed:

- JSON parsing for owned configuration files.
- `.csproj` XML parsing.
- C# source delimiter/string/comment balance scan.
- JavaScript syntax checks with Node for the customer booking page, admin booking page and `site.js`.
- CSS brace consistency check.
- Bash syntax check for `scripts/build-deployment.sh`.
- Scan for stale `DateTime.Now`, `DateTime.Today`, `.ToLocalTime()`, QuestPDF usage, TODO/FIXME markers and committed SQL passwords in owned source.
- Scan for state-changing Razor Page GET handlers.

## Important final verification on your machine

The execution environment used for this review does **not** contain the .NET SDK and cannot access the internet from its shell, so a real `dotnet restore` / `dotnet build` could not be executed here.

After extracting the hardened ZIP, run:

```bash
dotnet restore LaGrandeurSpa.Web.csproj
dotnet build LaGrandeurSpa.Web.csproj -c Release
```

Before pointing the new code at the live La Grandeur database, take a verified SQL backup. Then configure `ConnectionStrings:DefaultConnection` through user-secrets/environment variables as documented in `README.md` and start the app. `DatabaseSchemaManager` will apply the current in-place upgrades.

## Deliberate current product rules (not bugs)

- The calendar remains a **single-resource** spa calendar: only one booking can occupy a time period. There is no therapist/resource assignment model yet.
- PDF output is deliberately **browser Print / Save PDF**, not a server-generated QuestPDF file.
- SMTP remains optional; in-app booking/reminder functionality does not depend on SMTP being enabled.

## Startup migration hotfix — 11 Aug 2026

After the first local run against the existing `LaGrandeurSpaDb`, SQL Server exposed a same-batch DDL compilation issue in schema upgrade 1: `InvoiceIncludesHerbalTea` was added and then referenced later in the same SQL batch. SQL Server can compile the later statement before the new column is visible, causing error 207 (`Invalid column name`).

The fix keeps schema upgrade 1 atomic but executes it as three separate commands inside one EF Core transaction:
1. Add any missing invoice snapshot columns.
2. Backfill the new snapshot values.
3. Enforce NOT NULL constraints and restore the invoice index/service foreign key.

This makes the upgrade safe to retry after the failed startup without deleting or recreating the database. `_Layout.cshtml` was also made null-safe to remove the CS8602 build warning reported during the same verification run.
