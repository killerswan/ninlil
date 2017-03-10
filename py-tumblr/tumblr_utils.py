import datetime
#import json
import logging
import os
import os.path
import requests
from tumblpy import Tumblpy, TumblpyError
from zipfile import ZipFile, ZipInfo

from appconfig import read_config


def in_date_range(date, start = None, end = None):
    '''
    Check if a date is within a (possibly open) range.
    '''
    if end is not None and not date < end:
        return False
    
    if start is not None and not start <= date:
        return False

    return True


def choose_photo_url(photo):
    '''
    Given a Tumblr photo post,
    look at the "original_size" and "alt_sizes"
    to choose the largest file's URL.
    '''
    try:
        'maybe the original is available'
        url = photo['original_size']['url']
        return url

    except KeyError:
        'find the biggest alternate'
        max_height = 0
        url = None

        for alt in photo['alt_sizes']:
            if max_height < alt['height']:
                max_height = alt['height']
                url = alt['url']
        return url


def choose_download_prefix(type, blog_url, start_date, end_date):
    '''
    Given a start and end datetime, and a download type,
    choose an archive name prefix.
    '''

    if start_date is not None:
        start_ts = start_date.timestamp()
    else:
        start_ts = 0

    if end_date is not None:
        end_ts = end_date.timestamp()
    else:
        end_ts = datetime.datetime.utcnow().timestamp()

    return '%s_from_%s_dates_%s_to_%s' % (type, blog_url, start_ts, end_ts)


def save_photo_file(archive, prefix, url, id, timestamp):
    '''
    Given a photo URL, download and save it.
    '''

    '''derive it's name and date'''
    date = datetime.datetime.utcfromtimestamp(timestamp)
    metadata = ZipInfo(
            filename = '%s/%s_%s' % (prefix, id, os.path.basename(url)),
            date_time = (date.year, date.month, date.day, date.hour, date.minute, date.second),
        )

    'download it'
    photo_data = requests.get(url).content

    'save it in the archive'
    with archive.open(metadata, 'w') as dl:
        dl.write(photo_data)


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
        prefix = choose_download_prefix('photos', self.blog_url, start_date, end_date)
        zipfile_name = '%s.zip' % (prefix,)

        posts = self.query_posts(
                post_type = 'photo',
                start_date = start_date,
                end_date = end_date
            )

        with ZipFile(zipfile_name, 'w') as archive:
            for post in posts:
                for photo in post['photos']:
                    url = choose_photo_url(photo)
                    save_photo_file(archive, prefix, url, post['id'], post['timestamp'])

        return zipfile_name


    def delete_post(self, id):
        '''
        Delete a post by ID.
        '''
        try:
            # TODO: put this into a new Tumblpy method for post deletion
            return self.api.post(
                    blog_url = self.blog_url,
                    endpoint = 'post/delete',
                    params = { 'id': id }
                )
        except TumblpyError as ex:
            # TODO: put msg and error_code in the Tumblpy docs
            logging.error('Error %s deleting a post (id %s): %s' % (ex.error_code, id, ex.msg))
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

        logging.warning('Deleting %s posts in date range between %s and %s' % (len(posts), start_date, end_date))

        for post in posts:
            self.delete_post(post['id'])
