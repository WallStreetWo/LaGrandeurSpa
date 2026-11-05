# La Grandeur Spa — User Feedback Release v7

This hotfix extends the v6 booking-update workflow so that email is sent to both sides when a customer updates an eligible booking.

- The customer receives the existing booking-updated confirmation email.
- Every active administrator with an email address (including Kea) receives an admin booking-updated email.
- The admin email includes the customer name/contact details, updated appointment details, booking reference, and whether the revised booking has returned to Pending for confirmation.
- Existing in-app customer and admin notifications remain unchanged.
- Email failures remain best-effort side effects and do not roll back a successfully saved booking update.
- No database schema change is required.
- SMTP must be enabled and working for emails to be delivered.
