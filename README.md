# La Grandeur Spa Booking App

La Grandeur Spa is an ASP.NET Core Razor Pages booking application for customers and the spa administrator. The current hardened build targets **.NET 10** and **SQL Server / Azure SQL**.

## What the application includes

### Customer side

- Account registration and secure email/password sign-in with cookie authentication.
- Password visibility toggles, authenticated password change, forgot-password and time-limited password-reset flow.
- Account-created and password-changed security emails when SMTP is enabled.
- Calendar-first booking experience with monthly navigation.
- Live treatment availability and 15-minute booking intervals.
- Booking modal with treatment, duration, appointment date/time, price, herbal-tea offer and optional customer note.
- My Bookings page with upcoming appointments, history, cancellation, booking updates/rescheduling and invoice access.
- In-app notifications for booking submission, invoice availability, confirmation, cancellation and reminders.
- Email messages when SMTP is enabled.
- Privacy protection: customers can see occupied periods without seeing another customer's personal information.

### Admin / Kea side

- Dashboard with awaiting-confirmation count, today's appointments, next-seven-day confirmations and registered-customer count.
- Booking filters for Active, Pending, Confirmed, Completed, Cancelled, Declined and All.
- Pending booking confirmation/decline workflow.
- Confirmed booking completion/cancellation workflow.
- Weekly opening-hours management.
- Blocked-period management with conflict protection against existing active bookings.
- Treatment management for name, duration, price, calendar colour, availability and herbal-tea offer.
- Full admin calendar with customer details.

### Invoice, location and reminder features

- An invoice snapshot is created when a booking is received.
- Existing invoices/history keep the booked treatment name, duration, price and herbal-tea offer even if Kea later edits the service menu. An explicit customer booking update intentionally refreshes that booking’s snapshot to the newly selected treatment.
- Customers can access only their own invoices; admins can access all invoices.
- Invoice page is print-optimised for **Print / Save as PDF** in the browser.
- Invoice includes La Grandeur Spa banking details, cell number, exact Centurion address and a Google Maps link.
- Public Find us page embeds the business location and supports current-location directions with a safe HTTP fallback.
- Day-before and same-day in-app reminders work even when SMTP is disabled.
- When SMTP is enabled, reminder emails are sent as an additional delivery channel.

## Seeded treatment menu

- Back, Neck & Shoulder Massage — 30 min — R300
- Back, Neck & Shoulder Massage — 45 min — R450
- Swedish Full Body Massage — 60 min — R550
- Swedish Full Body Massage — 90 min — R750
- Deep Tissue Massage — 60 min — R650
- Deep Tissue Massage — 90 min — R850
- Sports Massage — 60 min — R650
- Sports Massage — 90 min — R850
- Foot Reflexology — 30 min — R350

The 90-minute massage options include the complimentary herbal-tea offer shown on the supplied La Grandeur menu.

## Important booking-integrity rules

The current application is a **single-resource booking calendar**. One appointment can occupy a particular time period at once. There is not yet a therapist/resource table.

A booking is split into 15-minute `BookingSlot` records. `BookingSlot.SlotStartLocal` has a unique SQL index. A 90-minute booking at 10:00 therefore reserves 10:00, 10:15, 10:30, 10:45, 11:00 and 11:15.

Booking creation also:

1. validates that the submitted time is on the server-approved booking grid;
2. checks direct appointment-range overlap;
3. rechecks availability inside a **serializable SQL transaction**; and
4. relies on the unique slot index as the final concurrency guard.

Cancelled and declined bookings release their slot records. Completed bookings remain in history.

`Booking.RowVersion` uses SQL Server `rowversion` optimistic concurrency. This prevents two simultaneous status actions, such as an admin confirmation and a customer cancellation, from silently overwriting each other.

## Database schema upgrades

Older versions of this project used only `EnsureCreatedAsync()`, which does not update an existing database when the C# model changes.

The current application keeps `EnsureCreatedAsync()` **only to create a completely new database**, then runs `DatabaseSchemaManager` for existing databases. The manager records non-destructive, versioned upgrades in the `AppSchemaMigrations` table.

Current upgrades cover:

- invoice snapshot columns and legacy invoice backfill;
- herbal-tea snapshot storage;
- non-cascading booking-to-service relationship;
- removal of stale slots from cancelled/declined bookings;
- verification/creation of the unique booking-slot index; and
- SQL Server `rowversion` concurrency protection.

**Do not delete or recreate the production database just to apply these changes.** Back up the database before every production deployment as normal operational practice.

Future database-model changes must be accompanied by a new explicit schema upgrade (or a planned move to a conventional EF Core migration chain). Do not add entity properties and assume `EnsureCreatedAsync()` will modify the live schema.

## Configure local development securely

The repository intentionally contains **no real SQL password**. `ConnectionStrings:DefaultConnection` is blank in `appsettings.json`.

From the project directory, configure local secrets with .NET user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=LaGrandeurSpaDb;User Id=sa;Password=YOUR_LOCAL_SQL_PASSWORD;TrustServerCertificate=True"
dotnet user-secrets set "SeedAdmin:FullName" "Kea"
dotnet user-secrets set "SeedAdmin:Email" "YOUR_ADMIN_EMAIL"
dotnet user-secrets set "SeedAdmin:Password" "YOUR_STRONG_ADMIN_PASSWORD"
```

For a disposable local development database, if you leave the seed-admin values at their development defaults, the temporary login is:

```text
Email: kea@lagrandeur.local
Password: ChangeMe!2026
```

The app refuses to seed those placeholder credentials on the first production launch when no administrator exists.

If a real admin already exists in the database, startup does not depend on the seed password afterward.

## Production environment variables

Use your host's secret/configuration store. ASP.NET Core converts double underscores to configuration separators:

```bash
export ConnectionStrings__DefaultConnection='YOUR_PRODUCTION_SQL_CONNECTION_STRING'
export SeedAdmin__FullName='Kea'
export SeedAdmin__Email='YOUR_REAL_ADMIN_EMAIL'
export SeedAdmin__Password='YOUR_STRONG_INITIAL_ADMIN_PASSWORD'
export Business__PublicBaseUrl='https://YOUR_FINAL_DOMAIN'
```

Never commit production connection strings, SMTP passwords or real user credentials to the repository.

## SMTP / email configuration

Email is optional for normal booking persistence and in-app notifications. Welcome emails, password-reset links, password-change alerts, booking-update emails and reminder emails require working SMTP.

Example environment configuration:

```bash
export Smtp__Enabled='true'
export Smtp__Host='smtp.example.com'
export Smtp__Port='587'
export Smtp__UseStartTls='true'
export Smtp__Username='YOUR_SMTP_USERNAME'
export Smtp__Password='YOUR_SMTP_PASSWORD'
export Smtp__FromName='La Grandeur Spa'
export Smtp__FromEmail='bookings@example.com'
```

Reminder thresholds are controlled by:

```text
EmailReminders:DayBeforeSendAfter = 09:00
EmailReminders:SameDaySendAfter   = 07:00
```

All appointment/business-time calculations use the configured Johannesburg timezone rather than the web server's local timezone.

## Build

```bash
dotnet restore LaGrandeurSpa.Web.csproj
dotnet build LaGrandeurSpa.Web.csproj -c Release
```

## Run

```bash
dotnet run --project LaGrandeurSpa.Web.csproj
```

Open the HTTPS URL printed by ASP.NET Core.

Before real bookings, Kea should confirm the weekly hours under **Admin → Availability**.

## Deployment ZIP

```bash
chmod +x scripts/build-deployment.sh
./scripts/build-deployment.sh
```

The script cleans old output, restores, performs a Release build, publishes the app and creates a timestamped deployment ZIP with the published files at the ZIP root.

## Security and reliability hardening in this build

- No database password in committed app settings.
- Production cookie requires HTTPS; authentication cookies are HttpOnly and SameSite=Lax.
- Authenticated cookies are revalidated against the database so deactivated users or role changes do not retain stale access.
- Login/registration endpoints are rate-limited per client IP.
- Duplicate-registration races are handled by the unique email index.
- Password hashes are transparently upgraded when the ASP.NET password hasher requests rehashing.
- Customer ownership is checked server-side for invoice, cancellation and booking-update operations.
- Password-reset links use time-limited ASP.NET Core Data Protection payloads tied to the user’s current password hash.
- Admin pages require the `AdminOnly` authorization policy.
- Server-side service, colour, duration, price, note and schedule validation is enforced.
- Booking time input is revalidated on the server; the browser dropdown is never trusted as the authority.
- Booking side-effect failures (notification/email) cannot falsely roll back or misreport a successfully committed booking.
- Security response headers include `X-Content-Type-Options`, `X-Frame-Options` and a strict referrer policy.
- Notification text is length-bounded before persistence.
- Notification bulk-read uses a direct database update instead of loading every row into memory.

## Operational checks before going live

Code hardening does not replace deployment operations. Before taking real customer bookings:

1. Take a verified SQL database backup and define an ongoing backup/restore policy.
2. Configure the real SQL secret in the hosting platform.
3. Configure and test the real admin account.
4. Confirm La Grandeur's real trading hours, business address, banking details and cancellation cutoff.
5. Configure SMTP if email delivery is required and send test messages.
6. Test concurrent booking attempts against the production-style database.
7. Test customer/admin flows on phone and desktop.
8. Test Print / Save PDF from the browsers the spa will actually use.
9. If hosted behind a reverse proxy/load balancer, configure trusted forwarded-header handling before relying on client-IP rate-limit partitions.
10. Monitor application logs and SQL storage after launch.

## User-testing feedback release

See `USER-FEEDBACK-UPDATE-2026-08-14.md` for the full list of account, booking-update, invoice and location changes implemented after the first user-testing round.
