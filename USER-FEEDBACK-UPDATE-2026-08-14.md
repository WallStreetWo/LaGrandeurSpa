# User Feedback Update — 14 August 2026

This release implements the first structured user-testing feedback received for the La Grandeur Spa Booking App.

## Account and password improvements

- Password show/hide buttons on login, registration, password reset and password change forms.
- "Forgot password?" link on the login page.
- Time-limited password reset links protected by ASP.NET Core Data Protection.
- Reset links are tied to the user's current password hash, so they become invalid after a successful password change.
- Authenticated users can change their password from the account/security link in the header.
- Password changes create an in-app security notification.
- Password changes/resets send a security email when SMTP is enabled.
- New customer registrations send a welcome/account-created confirmation email when SMTP is enabled.
- Password recovery gives the same external response whether or not an email address exists, reducing account-enumeration risk.

## Customer booking updates

- Eligible Pending and Confirmed bookings now have an "Update booking" action under My Bookings.
- Customers can update treatment, date, time and their note without cancelling the booking first.
- Booking updates use the existing collision protections: server-side slot validation, direct overlap checking, a serializable SQL transaction and the unique BookingSlot index.
- Availability checks exclude the booking being edited so its current reserved time can still appear while the customer edits it.
- Changing treatment/date/time on an already Confirmed booking returns it to Pending for Kea to confirm again.
- Note-only edits keep the existing confirmation status.
- The existing booking reference and invoice number are retained; the invoice snapshot is refreshed when treatment/schedule details change.
- Customer and admin in-app notifications are generated after a successful update.
- Customer booking-update email is sent when SMTP is enabled.

## Invoice/business information

Invoice now includes:

- Capitec Business Acc
- Account number 1055656901
- Booking reference as the recommended payment reference
- La Grandeur Spa cell number: 067 341 0424
- Address: Cnr R55 & Marais Rd Mall @ 55, Shop 60 Manovani, Centurion, 0157
- Google Maps address link

## Location and geolocation

- New public `Find us` page with the exact supplied business address.
- Embedded interactive Google map for the address.
- Google Maps search and directions actions.
- "Use my location" uses the browser Geolocation API on secure HTTPS connections.
- Because the current MonsterASP test site is temporarily HTTP-only, the feature automatically falls back to Google Maps directions when browser geolocation is blocked by secure-context rules.
- Phone call action included.

## Database impact

No new entity/database columns are required by this update. The existing database can be retained as-is.

## Deployment reminders

- SMTP must be enabled and working for welcome, password-reset, password-change and booking-update emails.
- `Business:PublicBaseUrl` / `Business__PublicBaseUrl` controls the base URL placed into account emails. Update it when moving from the temporary HTTP test URL to the final HTTPS/custom domain.
- Once HTTPS is available, remove the temporary `Security__RequireHttps=false` override so secure cookies, HSTS and browser geolocation can operate normally.
