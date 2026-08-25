Recorded the Windows user of the PC that made each booking

Description:
Bookings now carry the Windows account of the client PC they were made from, so a lunch booked by a
colleague or a receptionist on someone else's behalf can be attributed.

- Added the nullable `user_name`, `user_fullname` and `user_email` columns to `bookings`, with the
  matching migration and entity/configuration mapping, and carried them through both `MERGE` upsert
  branches in the booking repository.
- Added a `PcUser` domain area (`PcUserInfo`, resolver and context abstractions, `PcUserOptions`) and
  its web implementation: a capture middleware that reads the Windows identity IIS put on the request
  before the admin cookie can replace it, and an Active Directory resolver that enriches the account
  name with display name and e-mail. Every failure path degrades to "record the account name only";
  capture never blocks or fails a booking, and a null result is normal.
- Flowed the captured user from the booking view model through the booking requests and service into
  the stored booking, resolving it once per save rather than once per day.
- Added a "booked by" line to the employee confirmation e-mail, in French and English, shown only
  when there is a name to attribute the booking to.
- Added the `PcUser` configuration section to `config/app.json`.

Still open: the temporary `GET /whoami` diagnostic middleware, gated by `PcUser:Diagnostics`, must be
removed before production, and Windows authentication has only been exercised over localhost for a
single user — never from another PC on the domain.

Author: Massimo Fauro

Version:
