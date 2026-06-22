"""Version information utils for ETS2LA.
This file is used by a lot of frontend code to determine the current
version and to check for updates.
"""

import ETS2LA.variables as variables
import requests
import logging
import time
import git
import sys
import os

CACHE_DIR = f"{variables.PATH}cache"
CACHE_FILE = os.path.join(CACHE_DIR, "avatar_urls.txt")


def get_commit_url(repo, commit_hash):
    try:
        # Get the remote URL
        remote_url = repo.remotes.origin.url

        # Remove .git extension if present
        remote_url = remote_url.replace(".git", "")

        # Handle SSH format (git@github.com:user/repo)
        if remote_url.startswith("git@"):
            remote_url = remote_url.replace(":", "/")
            remote_url = remote_url.replace("git@", "https://")

        # Determine platform and create URL
        if "github.com" in remote_url:
            return f"{remote_url}/commit/{commit_hash}"
        elif "gitlab.com" in remote_url:
            return f"{remote_url}/-/commit/{commit_hash}"
        else:
            return ""
    except Exception:
        logging.warning(
            "Unable to get commit URL. Remote URL might not be set or is not supported."
        )
        return ""


def CheckForUpdate():
    return False # TODO: Maybe in the future re-enable updates... Though now that C# is going to be released this version is deprecated.
    try:
        repo = git.Repo()
        current_hash = repo.head.object.hexsha

        origin = repo.remotes.origin
        try:
            origin_state = origin.fetch(kill_after_timeout=4)
        except Exception:
            logging.warning(
                "Unable to fetch ETS2LA repository, update check will be skipped."
            )
            return False

        if len(origin_state) > 0:
            origin_hash = origin_state[0].commit.hexsha
        else:
            origin_hash = current_hash

        if current_hash != origin_hash:
            updates = []
            for commit in repo.iter_commits(f"{current_hash}..{origin_hash}"):
                summary = str(commit.summary)
                if "Merge" not in summary:  # Ignore merge commits
                    updates.append(
                        {
                            "author": commit.author.name,
                            "message": summary,
                            "description": commit.message.replace(summary, "").strip(),  # type: ignore
                            "time": commit.committed_date,
                            "url": get_commit_url(repo, commit.hexsha),
                            "hash": commit.hexsha,
                        }
                    )

            if (
                updates == []
            ):  # local commit(s) waiting to be pushed, send those instead
                for commit in repo.iter_commits():
                    summary = str(commit.summary)
                    if "Merge" not in summary:
                        updates.append(
                            {
                                "author": commit.author.name,
                                "message": summary,
                                "description": commit.message.replace(
                                    summary, ""
                                ).strip(),  # type: ignore
                                "time": commit.committed_date,
                                "url": get_commit_url(repo, commit.hexsha),
                                "hash": commit.hexsha,
                            }
                        )
                    if len(updates) >= 10:
                        break

                updates = updates[::-1]

                updates.append(
                    {
                        "author": "Backend",
                        "message": "DO NOT UPDATE!",
                        "description": "You have a local commit that is waiting to be pushed. Updating will clear the changes and stash them.",
                        "time": time.perf_counter(),
                        "url": "",
                        "hash": "local",
                    }
                )

            return updates
        else:
            return False
    except Exception:
        import traceback

        traceback.print_exc()
        return False


commits_save = []


def GetHistory():
    global commits_save
    try:
        # Return cached results if available
        if commits_save != []:
            return commits_save

        # Vars
        commits = []
        authors = {}

        # Get the git history as json
        repo = git.Repo(search_parent_directories=True)

        for commit in repo.iter_commits():
            summary = str(commit.summary)
            if "Merge" not in summary:  # Ignore merge commits like in CheckForUpdate
                commit_data = {
                    "author": commit.author.name,
                    "message": summary,
                    "description": commit.message.replace(summary, "").strip(),  # type: ignore
                    "time": commit.committed_date,
                    "url": get_commit_url(
                        repo, commit.hexsha
                    ),  # Add URL like CheckForUpdate,
                    "hash": commit.hexsha,
                }

                # Handle avatar URLs for first 100 commits
                if len(commits) < 20:
                    if commit.author.name not in authors:
                        if commit.author.name == "DylDev":
                            url = "https://api.github.com/users/DylDevs"
                        elif commit.author.name == "Lun":
                            url = "https://api.github.com/users/Lun011666"
                        else:
                            url = f"https://api.github.com/users/{commit.author.name}"

                        cached_url = load_cached_avatar_url(commit.author.name)
                        if cached_url:
                            authors[commit.author.name] = cached_url
                        else:
                            try:
                                response = requests.get(url, timeout=6)
                                if response.status_code == 200:
                                    data = response.json()
                                    avatar_url = data["avatar_url"]
                                    authors[commit.author.name] = avatar_url
                                    save_avatar_url_to_cache(
                                        commit.author.name, avatar_url
                                    )
                                else:
                                    authors[commit.author.name] = (
                                        "https://github.com/favicon.ico"
                                    )
                            except Exception:
                                print(
                                    f"Github API request was unsuccessful for author: {commit.author.name}. (Timed out)"
                                )
                                authors[commit.author.name] = (
                                    "https://github.com/favicon.ico"
                                )

                    commit_data["picture"] = authors[commit.author.name]

                commits.append(commit_data)

        commits_save = commits
        return commits
    except Exception:
        import traceback

        traceback.print_exc()
        return []


def Update():
    variables.UPDATE = True


def load_cached_avatar_url(username):
    if not os.path.exists(CACHE_DIR):
        os.makedirs(CACHE_DIR)

    if os.path.exists(CACHE_FILE):
        with open(CACHE_FILE, "r") as f:
            for line in f:
                parts = line.strip().split(": ")
                if parts[0] == username:
                    # Check if the cached URL is older than 24 hours
                    cached_time = float(parts[1])
                    if time.perf_counter() - cached_time < 24 * 3600:
                        return parts[2]
    return None


def save_avatar_url_to_cache(username, url):
    if not os.path.exists(CACHE_DIR):
        os.makedirs(CACHE_DIR)

    cached_urls = []
    if os.path.exists(CACHE_FILE):
        with open(CACHE_FILE, "r") as f:
            for line in f:
                parts = line.strip().split(": ")
                if parts[0] == username:
                    # Update the existing entry for the user
                    cached_urls.append(f"{username}: {time.perf_counter()}: {url}")
                else:
                    cached_urls.append(line.strip())

    # If the user was not found in the cache, add a new entry
    if username not in [line.split(": ")[0] for line in cached_urls]:
        cached_urls.append(f"{username}: {time.perf_counter()}: {url}")

    with open(CACHE_FILE, "w") as f:
        f.write("\n".join(cached_urls))


def check_python_version():
    # Check that Python version is supported
    supported_versions = [(3, 11, "x"), (3, 12, "x"), (3, 13, "x")]
    recomended_version = (3, 12, 7)

    major, minor, micro = sys.version_info[:3]
    accepted = [False] * 3  # Major, Minor, Micro
    for version in supported_versions:
        if version[0] == major:
            accepted[0] = True  # Major version accepted
        if version[1] == minor:
            accepted[1] = True  # Minor version accepted
        if version[2] == "x" and version[1] == minor:
            accepted[2] = True  # Any micro version of a minor version accepted
        if version[2] == micro:
            accepted[2] = True  # Micro version accepted

    if not all(accepted):
        current_version_formatted = f"{major}.{minor}.{micro}"
        recomended_version_formatted = (
            f"{recomended_version[0]}.{recomended_version[1]}.{recomended_version[2]}"
        )
        recomended_link = f"https://www.python.org/ftp/python/{recomended_version_formatted}/python-{recomended_version_formatted}-amd64.exe"
        error = f"Your Python version is {current_version_formatted}, which is not supported by ETS2LA. Download the recomended version ({recomended_version_formatted}) here:\n{recomended_link}"
        logging.error(error)
        return False

    return True
