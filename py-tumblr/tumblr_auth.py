import os
import os.path
from tumblpy import Tumblpy, TumblpyError
from appconfig import read_config


def oauth_initial_auth(callback_url):
    '''
    Get the auth URL and token secret.

    Our app should then save these and redirect the user to Tumblr's auth URL.
    '''
    conf = read_config()
    tum = Tumblpy(conf['consumer_key'], conf['consumer_secret'])

    auth_props = tum.get_authentication_tokens(callback_url=callback_url)

    'return Tumblr URL to be used for auth, and secret for verification'
    return auth_props['auth_url'], auth_props['oauth_token_secret']


def oauth_verify_user_token(secret, callback_token, callback_verifier):
    '''
    Given the user's token secret (from initial auth),
    token (from Tumblr's ref. to our callback) and
    token verifier (from Tumblr's ref. to our callback)
    derive the final OAuth token and secret.

    Our app should save these for the user.
    '''
    conf = read_config()
    tum = Tumblpy(conf['consumer_key'], conf['consumer_secret'], callback_token, secret)
    final = tum.get_authorized_tokens(callback_verifier)
    return final['oauth_token'], final['oauth_token_secret']
