import os
import hashlib
import requests
from dotenv import load_dotenv

load_dotenv()

def _env_list(key):
    value = os.getenv(key, "")
    return [item.strip() for item in value.split(",") if item.strip()]

REPOSITORY = "Juzlus/jRandomSkills"
DISCORD_USER_IDS = _env_list("DISCORD_USER_IDS")
EXTRA_USERS_GITHUB = _env_list("EXTRA_USERS_GITHUB")

CONTRIBUTORS_URL = "https://github.com/Juzlus/jRandomSkills/graphs/contributors"
AVATARS_DIR = "./.github/avatars"
AVATARS_RAW_URL = "https://raw.githubusercontent.com/Juzlus/jRandomSkills/main/.github/avatars"

MARKER_GITHUB = "[CONTRIBUTORS]"
MARKER_DISCORD = "[THANKS]"


def build_table(cells, max_per_row=6):
    rows = []
    for i in range(0, len(cells), max_per_row):
        row_cells = "".join(cells[i:i + max_per_row])
        rows.append(f"  <tr>{row_cells}</tr>")
    return "<table>\n" + "\n".join(rows) + "\n</table>"


def make_cell(avatar_url, name, profile_url=None):
    if profile_url:
        return (
            f'<td align="center">'
            f'<a href="{profile_url}">'
            f'<img src="{avatar_url}" width="75" height="75" style="border-radius:50%" alt="{name}"/>'
            f'<br/><sub><b>{name}</b></sub>'
            f'</a>'
            f'</td>'
        )
    return (
        f'<td align="center">'
        f'<img src="{avatar_url}" width="75" height="75" style="border-radius:50%" alt="{name}"/>'
        f'<br/><sub><b>{name}</b></sub>'
        f'</td>'
    )


def save_avatar(source_url, filename):
    os.makedirs(AVATARS_DIR, exist_ok=True)
    filepath = os.path.join(AVATARS_DIR, filename)
    response = requests.get(source_url, timeout=10)
    if response.status_code != 200:
        print(f"Failed to download avatar: {source_url}")
        return None
    with open(filepath, "wb") as f:
        f.write(response.content)
    return f"{AVATARS_RAW_URL}/{filename}"


def fetch_github_user(login):
    response = requests.get(f"https://api.github.com/users/{login}", timeout=10)
    if response.status_code != 200:
        print(f"GitHub API Error for user '{login}': Status {response.status_code}")
        return None
    return response.json()


def get_github_contributors(repo, extra_logins):
    try:
        url = f"https://api.github.com/repos/{repo}/contributors"
        response = requests.get(url, timeout=10)
        if response.status_code != 200:
            print(f"GitHub API Error: Status {response.status_code}")
            return ""

        seen = set()
        cells = []

        for user in response.json():
            name = user.get("login")
            if not name or name == "web-flow":
                continue
            seen.add(name.lower())
            cells.append(make_cell(
                avatar_url=f"{user['avatar_url']}&s=75",
                name=name,
                profile_url=user["html_url"],
            ))

        for login in extra_logins:
            if login.lower() in seen:
                print(f"Skipping duplicate extra user: {login}")
                continue
            user = fetch_github_user(login)
            if not user:
                continue
            seen.add(login.lower())
            cells.append(make_cell(
                avatar_url=f"{user['avatar_url']}&s=75",
                name=user["login"],
                profile_url=user["html_url"],
            ))

        return build_table(cells) if cells else ""

    except Exception as e:
        print(f"GitHub Fetch Error: {e}")
        return ""


def get_discord_users(user_ids):
    token = os.getenv("DISCORD_BOT_TOKEN")

    if not token:
        print("DISCORD_BOT_TOKEN not found")
        return ""

    headers = {
        "Authorization": f"Bot {token}"
    }

    cells = []

    for uid in user_ids:
        try:
            response = requests.get(
                f"https://discord.com/api/v10/users/{uid}",
                headers=headers,
                timeout=10
            )

            if response.status_code != 200:
                print(f"Discord API: Status {response.status_code}")
                print(response.text)
                continue

            data = response.json()

            name = (
                data.get("global_name")
                or data.get("username")
            )

            avatar_hash = data.get("avatar")

            if avatar_hash:
                source_url = (
                    f"https://cdn.discordapp.com/avatars/"
                    f"{uid}/{avatar_hash}.png?size=128"
                )
            else:
                discriminator = data.get("discriminator", "0")

                if discriminator == "0":
                    default_index = (int(uid) >> 22) % 6
                else:
                    default_index = int(discriminator) % 5

                source_url = (
                    f"https://cdn.discordapp.com/embed/avatars/"
                    f"{default_index}.png"
                )

            uid_hash = hashlib.sha1(uid.encode()).hexdigest()[:12]
            filename = f"discord_{uid_hash}.png"

            avatar_url = save_avatar(source_url, filename)

            if not avatar_url:
                continue

            cells.append(
                make_cell(
                    avatar_url=avatar_url,
                    name=name
                )
            )

        except Exception as e:
            print(f"Discord API Error for: {e}")

    return build_table(cells) if cells else ""


def process_file(filename_input, filename_output, github_elements, discord_elements, is_pl):
    input_path = f"./.github/{filename_input}"
    output_path = f"./{filename_output}"

    if not os.path.exists(input_path):
        print(f"Error: File {input_path} does not exist")
        return

    with open(input_path, "r", encoding="utf-8") as f:
        content = f.read()

    if is_pl:
        info_github = (
            f'Jeśli jesteś <a href="{CONTRIBUTORS_URL}">współtwórcą</a> i chcesz, '
            f'aby Twój profil został usunięty z tej listy, skontaktuj się ze mną.'
        )
        info_discord = (
            "Jeśli jesteś na tej liście i chcesz usunąć swój profil, skontaktuj się ze mną."
        )
    else:
        info_github = (
            f'If you are a <a href="{CONTRIBUTORS_URL}">contributor</a> and want your profile '
            f'removed from this list, please contact me.'
        )
        info_discord = (
            "If you are on this list and want your profile removed, please contact me."
        )

    if MARKER_GITHUB in content and github_elements:
        content = content.replace(MARKER_GITHUB, (
            f'<div align="center">\n\n'
            f'{github_elements}\n\n'
            f'<sub>{info_github}</sub>\n\n'
            f'</div>'
        ))
    elif MARKER_GITHUB in content:
        print(f"Warning: No GitHub data to replace marker in {input_path}")

    if MARKER_DISCORD in content and discord_elements:
        content = content.replace(MARKER_DISCORD, (
            f'<div align="center">\n\n'
            f'{discord_elements}\n\n'
            f'<sub>{info_discord}</sub>\n\n'
            f'</div>'
        ))
    elif MARKER_DISCORD in content:
        print(f"Warning: No Discord data to replace marker in {input_path}")

    with open(output_path, "w", encoding="utf-8") as f:
        f.write(content)

    print(f"Success: Created {output_path}")


def main():
    print(f"DISCORD_USER_IDS loaded: {len(DISCORD_USER_IDS)} user(s)")
    print(f"EXTRA_USERS_GITHUB loaded: {len(EXTRA_USERS_GITHUB)} user(s)")

    print("Fetching GitHub contributors...")
    github_elements = get_github_contributors(REPOSITORY, EXTRA_USERS_GITHUB)

    print("Fetching Discord users...")
    discord_elements = get_discord_users(DISCORD_USER_IDS)

    files = [("en.md", "README.md", False), ("pl.md", "README-PL.md", True)]

    for filename_input, filename_output, is_pl in files:
        process_file(filename_input, filename_output, github_elements, discord_elements, is_pl)


if __name__ == "__main__":
    main()