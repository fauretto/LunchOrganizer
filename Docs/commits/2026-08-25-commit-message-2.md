Restored the administration login under Windows Authentication and hashed its passwords

Description:
Enabling Windows Authentication in IIS had left the administration page reachable by every domain user with no login at all: IIS puts the caller's Windows principal on the request, and the bare authorization check asked only for an authenticated user, which that principal satisfies. Signing in now issues a marker claim that only the admin cookie path can produce, and the default authorization policy requires both that claim and the cookie scheme, so the administration page and the CSV export are closed again to anyone who has not signed in with admin-users.json credentials. A cookie issued without the claim is signed out on its next request.

Replaced the plain-text and unsalted SHA-256 password handling with PBKDF2 (HMACSHA256, 210000 iterations, a random salt per password) in a hasher shared by the web application and its tools, and removed both weaker formats: a stored value that is not a pbkdf2-sha256 token no longer authenticates anyone, and is reported in the log. Added the LunchOrganizer.AdminHash console tool, which turns a password into the token to paste into admin-users.json, and unit tests covering the round-trip and the rejected formats.

Duplicate entries for one username in admin-users.json are now logged as a warning. Only the first was ever used, which silently shadowed a newly hashed password.

Added the session handover for the day.

The administration login has not yet been exercised in a browser, and the deployed config\admin-users.json still holds the previous entry until the publish script is re-run.

Author: Massimo Fauro

Version:
