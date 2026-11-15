# Text messages (SMS): setup

Texts are built and off. They need the `SmsMessaging` flag and a configured provider. Design:
[ADR-0021](../adr/0021-partner-messages-by-text.md).

## Try it locally

Development uses the Fake provider: nothing is sent. Sign in, open **Pace and notifications › Set up
text messages**, enter any number (for example `+1 555 555 0101`), then open `/dev/texts` to read
the code and every text the app "sends". Use the same page to text the app as that number: a reply,
then `SEND`, `STOP`, `START` or `HELP`.

## Turn it on with Twilio (owner steps)

1. Create a Twilio account and buy a number that can send and receive SMS in the launch market.
   - US: register the number for A2P 10DLC (or use a verified toll-free number) before sending.
   - UK: use a long number; an alphanumeric sender ID can't receive replies.
2. On the number (or a Messaging Service), set **A message comes in** to
   `https://<public host>/sms/twilio`, HTTP POST. Keep Twilio's Advanced Opt-Out on so STOP/START/HELP
   are honoured at the carrier level too.
3. Store the settings as secrets (user-secrets locally, Key Vault in Azure), never in the repo:

   ```
   Sms__Provider=Twilio
   Sms__PublicBaseUrl=https://<public host>
   Sms__Twilio__AccountSid=AC...
   Sms__Twilio__AuthToken=...
   Sms__Twilio__FromNumber=+1...        # or Sms__Twilio__MessagingServiceSid=MG...
   Sms__DefaultCallingCode=1            # 44 for the UK
   FeatureManagement__SmsMessaging=true
   ```

   `PublicBaseUrl` must be the exact https address Twilio calls, or every webhook is refused
   (the signature covers the URL).
4. Update the privacy notice to name the SMS provider as a processor and say what texts contain.
5. Set a spending limit or usage trigger in Twilio. The app caps each person at
   `Sms:DailyTextsPerUser` (20) message texts a day.

## What to check after turning it on

- Turning on texts sends a code; a wrong code 5 times needs a new code.
- A message from the partner arrives as a text within about 15 seconds (after quiet hours end).
- Replying by text echoes the reply; `SEND` shares it; the partner sees it in the app.
- `STOP` stops texts at once; `START` resumes.
