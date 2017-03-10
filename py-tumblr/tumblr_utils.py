import datetime
#import json
import logging
import os
import os.path
import requests
from tumblpy import Tumblpy, TumblpyError

from tumblr_auth import read_config


def in_date_range(date, start = None, end = None):
    '''
    Check if a date is within a (possibly open) range.
    '''
    if end is not None and not date < end:
        return False
    
    if start is not None and not start <= date:
        return False

    return True


def save_photo_file(url, blog_name, id):
    '''
    Given a photo URL, download and save it.
    '''
    download_name = 'dl_%s_%s_%s' % (
            blog_name,
            id,
            os.path.basename(url),
        )
    photo_data = requests.get(url).content
    with open(download_name, 'wb') as dl:
        #print('Saving %s as %s.' % (url, download_name))
        dl.write(photo_data)
    return download_name



class TumblrUtils:
    '''
    A few utilities beyond what Tumblpy provides.
    '''

    def __init__(self, oauth_token, oauth_token_secret, blog_url):
        '''
        Create an API instance for a given Given a pair of (final) oauth creds.
        '''
        conf = read_config()
        self.api = Tumblpy(
                conf['consumer_key'],
                conf['consumer_secret'],
                oauth_token,
                oauth_token_secret,
            )
        self.blog_url = blog_url


    def query_posts(self, post_type = None, start_date = None, end_date = None):
        '''
        Get a bunch of posts.

        Possible types: text, quote, link, answer, video, audio, photo, chat.

        Warning: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        '''

        resp = self.api.posts(blog_url = self.blog_url, post_type = post_type)
        # TODO: add limit and offset to Tumblpy (or remove from Tumblpy docs)
        
        def in_range(post):
            post_date = datetime.datetime.utcfromtimestamp(post['timestamp'])
            return in_date_range(post_date, start_date, end_date)

        matching_posts = filter(in_range, resp['posts'])

        #print(json.dumps(matching_posts, sort_keys=True, indent=3))
        return matching_posts


    def save_photos(self, start_date = None, end_date = None):
        '''
        Download and save a bunch of photos.

        Warning: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        '''
        posts = self.query_posts(
                post_type = 'photo',
                start_date = start_date,
                end_date = end_date
            )

        photo_files = []

        for post in posts:

            for photo in post['photos']:
                try:
                    'maybe the original is available'
                    url = photo['original_size']['url']
                    photo_files.append(save_photo_file(url, post['blog_name'], post['id']))

                except KeyError:
                    'find the biggest alternate'
                    max_height = 0
                    url = None

                    for alt in photo['alt_sizes']:
                        if max_height < alt['height']:
                            max_height = alt['height']
                            url = alt['url']
                    save_photo_file(url)
                    photo_files.append(save_photo_file(url, post['blog_name'], post['id']))


    def delete_post(self, id):
        '''
        Delete a post by ID.
        '''
        try:
            # TODO: put this into a new Tumblpy method for post deletion
            return self.api.post(
                    blog_url = self.blog_url,
                    endpoint = 'post/delete',
                    params = { 'id': 158169280796 }
                )
        except TumblpyError as ex:
            # TODO: put msg and error_code in the Tumblpy docs
            logging.error('Error %s deleting a post: %s' % (ex.error_code, ex.msg))
            return None


    def delete_posts(self, post_type = None, start_date = None, end_date = datetime.datetime(1980,1,1)):
        '''
        Delete a bunch of posts!!

        Warning: the limit and offset properties aren't yet supported,
        so the start and end dates will filter what the API returns naively.
        '''
        posts = list(self.query_posts(
                post_type = post_type,
                start_date = start_date,
                end_date = end_date
            ))

        logging.warning('deleting %s posts in date range between %s and %s' % (len(posts), start_date, end_date))

        for post in posts:
            self.delete_post(post['id'])
