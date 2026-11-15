# ADR-0020: Partner messages by text (SMS), opt-in per partner

Status: accepted (2026-11-15). Built and off in Production behind `SmsMessaging` until an SMS
provider account, sender number and privacy notice wording are in place.

## Context

The owner wants partners to be able to get each other's messages by text and answer by text,
instead of only reading and replying on the site. Texts are less private than the app: they stay
on the phone and its lock screen, pass through the SMS provider and carrier, and can't be
withdrawn. They also bypass the coach and the preview step unless we rebuild those over text.

## Decision

- **Opt-in per person, never by the partner.** `/settings/texts`: the user types a number, ticks an
  explicit agreement to texts, and enters a 6-digit code texted to that number (hashed, 10 minutes,
  5 tries, 3 codes an hour). Turning texts on writes a `ConsentRecord` (`SmsMessaging`, fingerprint
  of the number, never the number). Turning off deletes the number and withdraws the consent. A
  verified number belongs to one account. The partner is never told whether texts are on.
- **What a text says is the recipient's choice.** Default: a fixed notice that something arrived
  (no content, as ADR-0011). Optional: the message itself, cut at 480 characters with a link. When
  the recipient chose that, the sender's preview says withdrawing can't remove it from the partner's
  phone (based on the setting alone, so it reveals nothing about safety settings).
- **Texts ride on notifications.** A dispatcher texts in-app notifications once they are visible,
  so mute, quiet hours, pause, withdrawal and the safety hold apply with no new rules. Safety is
  checked again at send time. Each notification is claimed in `sms_deliveries` (unique) before
  sending, so it is texted at most once across instances. Only messages that arrive after texts
  were turned on (or back on after STOP) are texted; nothing older than 36 hours; at most 20 a day.
- **Replies keep consent.** A texted reply becomes the user's private draft (a reply to the latest
  message they received). Its exact text is echoed back; only `SEND` within 30 minutes shares it,
  through `IMessagingService.ShareAsync` with the echoed fingerprint, so the consent ledger, rate
  limit, pause and safety checks are the same as the app. Any other text replaces the waiting
  reply, which stays a private draft. An edit in the app in between means nothing is shared.
  `YES` is not a send word because carriers treat it as an opt-in keyword.
- **STOP / START / HELP** follow carrier conventions. STOP marks the number stopped at once (the
  provider sends the confirmation); START resumes; HELP explains. Unknown numbers get one neutral
  line.
- **Safety.** No safety answer, safety mode state, note, intake or exercise content is ever texted.
  A user whose own safety settings hold joint flows gets no texts and their texts are not drafted
  or passed on; the reply is a neutral "Please open the app to continue." The support page tips
  mention that texts stay on the phone.
- **Provider.** `ISmsSender` with a Twilio implementation (REST API, no SDK) and an in-memory Fake
  for Development with a simulator at `/dev/texts`. The Twilio webhook `/sms/twilio` is 404 while
  texts are off and 403 unless the `X-Twilio-Signature` matches the configured public URL.
  Credentials come from user-secrets or Key Vault. Logs carry ids only, never numbers, codes or text.

## Consequences

- Texted content can't be withdrawn or erased; the UI says so to both people.
- Coach suggestions aren't available over text; the confirm text points to the app.
- The privacy notice must name the SMS provider as a processor before the flag is turned on, and
  the provider needs a registered sender (US: A2P 10DLC or toll-free verification; UK: an alphanumeric
  sender can't receive replies, so a number is needed). See `docs/operations/sms.md`.
