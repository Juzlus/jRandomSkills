import requests
import os

# Konfiguracja
USER_IDS = ["284780352042434570"]
MARKER = "[THANKS]"

def get_html_for_user(user_id):
    try:
        url = f"https://pfpfinder.com/api/discord/user/{user_id}"
        response = requests.get(url, timeout=10)
        data = response.json()

        name = data.get("global_name") or data.get("username")
        avatar = data.get("avatar")
        github_user = data.get("username")

        return f"""
<a href="https://github.com/{github_user}" title="{name}">
    <img src="{avatar}" alt="{name}" width="75" height="75" style="border-radius:50%">
</a>
"""
    except Exception as e:
        print(f"Błąd API: {e}")
        return ""

def update_file(file_path, html_content):
    if not os.path.exists(file_path):
        print(f"❌ Plik {file_path} nie istnieje!")
        return

    with open(file_path, "r", encoding="utf-8") as f:
        content = f.read()

    if MARKER not in content:
        print(f"⚠️ Marker {MARKER} nie został znaleziony w {file_path}")
        return

    # Zamiana markera na HTML
    new_content = content.replace(MARKER, html_content)

    with open(file_path, "w", encoding="utf-8") as f:
        f.write(new_content)

    print(f"✅ Zaktualizowano: {file_path}")

def main():
    elements = "".join([get_html_for_user(uid) for uid in USER_IDS])
    full_html = f'<div align="center">\n{elements}\n</div>'

    files = ["../README.md", "../README-PL.md"]

    for file in files:
        update_file(file, full_html)

if __name__ == "__main__":
    main()