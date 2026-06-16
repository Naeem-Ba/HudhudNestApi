# طريقة التطبيق السريعة

1. فك ضغط هذه الحزمة.
2. انسخ المجلدات التالية إلى جذر Repository `PropertyApi`:

```text
.github
ci
deploy
docs
README-CICD-AR.md
```

3. نفذ:

```powershell
git add .github/workflows/ci.yml .github/CODEOWNERS.example ci deploy docs README-CICD-AR.md
git commit -m "CI/CD: add secure pipeline with format, vulnerability, Trivy, coverage and staged deploy gates"
git push
```

4. اضبط GitHub Environments وSecrets كما هو موضح في `README-CICD-AR.md`.
5. فعّل Branch Protection واجعل Checks الجديدة مطلوبة قبل merge.
