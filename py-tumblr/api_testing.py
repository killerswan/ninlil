#!/usr/bin/env python3

from tumblpy import Tumblpy, TumblpyError

# TODO: abstract these
consumer_key = 
consumer_secret = 


def getToken():

    tum = Tumblpy(consumer_key, consumer_secret)

    # TODO: make a route which handles this callback
    auth_props = tum.get_authentication_tokens(callback_url='localhost')
    auth_url = auth_props['auth_url']

    oauth_token_secret = auth_props['oauth_token_secret']

    # TODO: automatically redirect to (or load) this page
    print('OAUTH_TOKEN_SECRET: %s' % oauth_token_secret)
    print('Connect with Tumblr via %s' % auth_url)


# derived with auth URL
oauth_token_secret = 

# TODO: on the route which handles the callback, receive and verify
oauth_token = 
oauth_verifier = 


def verifyToken():
    tum = Tumblpy(consumer_key, consumer_secret, oauth_token, oauth_token_secret)
    authorized_tokens = tum.get_authorized_tokens(oauth_verifier)

    final_oauth_token = authorized_tokens['oauth_token']
    final_oauth_token_secret = authorized_tokens['oauth_token_secret']
    
    print('Token: %s\nSecret: %s' % (final_oauth_token, final_oauth_token_secret))


final_oauth_token = 
final_oauth_token_secret = 
    
import json
import requests
import datetime
import os.path

def downloadPhotos():
    tum = Tumblpy(
            consumer_key,
            consumer_secret,
            final_oauth_token,
            final_oauth_token_secret,
        )
    resp = tum.posts(
            blog_url = 'kevin-cantu.tumblr.com',
            # text, quote, link, answer, video, audio, photo, chat
            post_type = 'photo',
            # TODO: add these to Tumblpy (or remove from Tumblpy docs)
            #limit = 1000,
            #offset = 0,
        )
    
    print(json.dumps(resp, sort_keys=True, indent=3))

    for post in resp['posts']:
        date = datetime.datetime.utcfromtimestamp(post['timestamp'])

        def save_photo_file(url):
            # save the photo
            download_name = 'dl_%s_%s_%s' % (
                    post['blog_name'],
                    post['id'],
                    os.path.basename(url),
                )
            photo_data = requests.get(url).content
            with open(download_name, 'wb') as dl:
                print('Saving %s as %s.' % (url, download_name))
                dl.write(photo_data)

        for photo in post['photos']:
            try:
                'maybe the original is available'
                url = photo['original_size']['url']
                save_photo_file(url)

            except KeyError:
                'find the biggest alternate'
                max_height = 0
                url = None

                for alt in photo['alt_sizes']:
                    if max_height < alt['height']:
                        max_height = alt['height']
                        url = alt['url']
                save_photo_file(url)


def deletePhotos():
    tum = Tumblpy(
            consumer_key,
            consumer_secret,
            final_oauth_token,
            final_oauth_token_secret,
        )

    try:
        # TODO: put this into a new Tumblpy method for post deletion
        tum.post(
                blog_url = 'kevin-cantu.tumblr.com',
                endpoint = 'post/delete',
                params = { 'id': 158169280796 }
            )
    except TumblpyError as ex:
        # TODO: put msg and error_code in the Tumblpy docs
        print(ex.msg)
        print(ex.error_code)


    

if __name__ == '__main__':
    #getToken()
    #verifyToken()
    #downloadPhotos()
    #deletePhotos()
    print('noop!')

