# Deploy v6 User-Feedback Release to MonsterASP

## Do not delete

Do **not** delete:

- the MonsterASP website service;
- the MSSQL database;
- the existing environment variables; or
- customer/booking data.

This release requires **no new database columns**.

## 1. Build locally

From the updated source folder:

```bash
dotnet clean
dotnet restore
dotnet build
dotnet publish LaGrandeurSpa.Web.csproj -c Release -o ./publish-v6
```

Create the deployment ZIP with the publish files at the ZIP root:

```bash
cd publish-v6
zip -r ../lagrandeurspa-publish-v6.zip .
cd ..
```

## 2. MonsterASP environment variables

Keep the existing database and seed-admin values.

Current HTTP test-host configuration:

```text
Security__RequireHttps=false
Business__PublicBaseUrl=http://lagrandeurspa.runasp.net
```

For Gmail SMTP, verify:

```text
Smtp__Enabled=true
Smtp__Host=smtp.gmail.com
Smtp__Port=587
Smtp__UseStartTls=true
Smtp__Username=<Gmail address>
Smtp__Password=<Google App Password>
Smtp__FromEmail=<same Gmail address>
Smtp__FromName=La Grandeur Spa
```

The forgot-password flow cannot deliver reset links unless SMTP is working.

## 3. Upload

In MonsterASP:

1. Websites → `lagrandeurspa.runasp.net` → Files.
2. Open the outer `/wwwroot` folder.
3. Upload `lagrandeurspa-publish-v6.zip`.
4. Unzip into the current `/wwwroot`.
5. Enable overwrite/replace existing files.
6. Restart the application/AppPool after extraction.
7. Delete the uploaded ZIP after a successful extraction.

Do not delete the inner `/wwwroot` folder from the published ASP.NET app; it contains CSS, JavaScript and images.

## 4. Test in this order

1. Login password show/hide toggle.
2. Register a fresh customer with a real test email and confirm the welcome email arrives.
3. Use Forgot password and confirm the reset email arrives.
4. Reset the password and confirm the password-changed email arrives.
5. Log in and use Account Security to change the password again.
6. Make a Pending booking.
7. Open My Bookings → Update booking and move it to another available time.
8. Confirm it as Kea, then update treatment/date/time again and verify it returns to Pending.
9. Check the updated invoice banking details, address and latest treatment/price.
10. Open Find us and test the embedded map/directions.

## 5. When HTTPS becomes available

Change:

```text
Security__RequireHttps=true
Business__PublicBaseUrl=https://lagrandeurspa.runasp.net
```

or replace the public base URL with the final custom HTTPS domain.

Browser-native precise geolocation will then be available to supported browsers. The location page already falls back to Google Maps directions while the temporary host remains HTTP-only.
