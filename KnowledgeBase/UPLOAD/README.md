# Knowledge Base Upload Staging

This folder is a temporary staging area for files that need to be added to the
CLV CivilTools GitHub repository.

## Permanent file

`README.md` is intentionally retained in this folder.

## Temporary upload workflow

1. Place temporary files to be added to GitHub in this folder.
2. Run:

   `KnowledgeBase\Scripts\KB-Upload.ps1`

3. Review the staged changes in Visual Studio.
4. Commit and push the changes through the normal Git workflow.
5. After the uploaded files have been used, run:

   `KnowledgeBase\Scripts\KB-Cleanup.ps1`

6. Review the staged cleanup changes in Visual Studio.
7. Commit and push the cleanup through the normal Git workflow.

## Important

- The scripts are intended for the `work` branch.
- The scripts do not automatically commit or push.
- Temporary files must remain inside `KnowledgeBase/UPLOAD/`.
- `README.md` is never removed by the cleanup script.
- `UPLOAD_MANIFEST.txt` is generated automatically and records the temporary
  files staged by `KB-Upload.ps1`.
- The UPLOAD folder is a Git staging/workflow area. It is not part of the
  published `CLV_CAD_KNOWLEDGE_BASE` website.
