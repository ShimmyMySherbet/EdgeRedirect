# EdgeRedirect
A simple tool to redirect web requests from Microsoft edge/bing to your default browser/Google

## How does it work?
Edge Redirect detects when Microsoft Edge is launched via a WMI watcher. 
It then captures the process's command line arguments to detect if it's launching a URL.
If the URL is a bing search url, it translates it to a Google search URL. 
Edge Redirect then kills the edge process tree, and launches your default browser with the URL.

This program runs in the background and does require administrator rights for the WIMI watcher.
