Files in this directory are placeholders only. On the server create:

- `postgres_password.txt` — a long random database password;
- `profile_token.txt` — a long random token shared only by the game and profile containers;
- `dtls_certificate.pem` — the full server certificate chain;
- `dtls_private_key.pem` — the matching unencrypted private key.

The actual files are ignored by Git. Limit access to the deployment account (`chmod 600`).

