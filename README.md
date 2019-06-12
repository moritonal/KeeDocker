# KeeDocker

KeeDocker is a plugin that adds support for an unofficial `docker://` scheme in KeePass. It's primary role is to allow the following workflow:

URL: `docker://moritonal/ssh-over-tor:latest {USERNAME}@bonner.is`

When the URL is double-clicked, the plugin will intercept the process call and perform the following actions:

* Create a docker container with the command `docker create -it --rm` plus anything after `docker://`.
* Copy all the attachments to the KeePass entry directly via an stdin pipeline into the container at `/tmp`.
* Start the container and attach a terminal.
